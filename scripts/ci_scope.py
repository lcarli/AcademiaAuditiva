"""Conservative CI scope selection and exact-commit deployment verification."""

import argparse
import json
import os
import re
import subprocess
from pathlib import Path
from xml.etree import ElementTree


LANDING_FILES = {
    "AcademiaAuditiva/Views/Home/Index.cshtml",
    "Tests/AcademiaAuditiva.IntegrationTests/HomePageTests.cs",
}
RESOURCE_FILES = {
    "AcademiaAuditiva/Resources/SharedResources.resx",
    "AcademiaAuditiva/Resources/SharedResources.pt-BR.resx",
    "AcademiaAuditiva/Resources/SharedResources.fr-CA.resx",
}
DEPLOYMENT_FILES = {
    "Dockerfile",
    "Directory.Build.props",
    "Directory.Packages.props",
    "global.json",
    ".github/workflows/cd.yml",
}
POLICY_FILES = {
    ".github/workflows/ci.yml",
    "scripts/ci_scope.py",
    "scripts/test_ci_scope.py",
}


def is_documentation(path):
    return (
        path in {"readme.md", "README.md", "CONTRIBUTING.md", "LICENSE"}
        or path.startswith("docs/")
        and Path(path).suffix.lower() in {".md", ".png", ".jpg", ".jpeg", ".webp", ".svg", ".gif", ".pdf"}
    )


def resource_snapshot(content):
    root = ElementTree.fromstring(content)
    entries = {}
    metadata = [root.tag, root.attrib, root.text]
    for element in root:
        element.tail = None
        serialized = ElementTree.tostring(element, encoding="unicode")
        if element.tag != "data":
            metadata.append(serialized)
            continue
        name = element.attrib["name"]
        if name in entries:
            raise ValueError(f"Duplicate resource key: {name}")
        entries[name] = serialized
    return metadata, entries


def home_resources_only(before, after):
    if before is None or after is None:
        return False
    old_metadata, old_entries = resource_snapshot(before)
    new_metadata, new_entries = resource_snapshot(after)
    if old_metadata != new_metadata:
        return False
    changed = {
        key for key in old_entries.keys() | new_entries.keys()
        if old_entries.get(key) != new_entries.get(key)
    }
    for key in changed:
        if not key.startswith("Home."):
            return False
        for entry in (old_entries.get(key), new_entries.get(key)):
            if entry is None:
                continue
            element = ElementTree.fromstring(entry)
            if element.attrib.keys() - {"name", "{http://www.w3.org/XML/1998/namespace}space"}:
                return False
            if len(element) != 1 or element[0].tag != "value" or element[0].attrib or len(element[0]):
                return False
    return True


def classify(paths, read_resource):
    if paths and all(is_documentation(path) for path in paths):
        return "docs"
    if not paths:
        return "full"
    for path in paths:
        if is_documentation(path) or path in LANDING_FILES:
            continue
        if path.startswith("AcademiaAuditiva/wwwroot/img/landing/") and path.endswith(".webp"):
            continue
        if path in RESOURCE_FILES and home_resources_only(*read_resource(path)):
            continue
        return "full"
    return "landing"


def deployment_changed(paths):
    return any(
        path.startswith(("AcademiaAuditiva/", "infra/")) or path in DEPLOYMENT_FILES
        for path in paths
    )


def git(*arguments):
    return subprocess.run(
        ["git", *arguments], check=True, capture_output=True, encoding="utf-8"
    ).stdout


def revision_file(revision, path):
    if not git("ls-tree", "--name-only", revision, "--", path).strip():
        return None
    return git("show", f"{revision}:{path}")


def impact(event_name, event, sha):
    if not re.fullmatch(r"[0-9a-f]{40}", sha):
        raise ValueError("A full Git commit SHA is required.")
    if event_name in {"workflow_dispatch", "workflow_call", "schedule"}:
        return {"sha": sha, "mode": "full", "deploy": event_name != "schedule", "infra": True, "files": []}
    if event_name == "pull_request":
        pull = event["pull_request"]
        head = pull["head"]["sha"]
        base = git("merge-base", pull["base"]["sha"], head).strip()
    elif event_name == "push":
        base, head = event["before"], sha
        if base == "0" * 40:
            paths = git("ls-tree", "-r", "--name-only", "-z", head).rstrip("\0").split("\0")
            return {"sha": sha, "mode": "full", "deploy": deployment_changed(paths), "infra": True, "files": paths}
    else:
        raise ValueError(f"Unsupported CI event: {event_name}")
    paths = sorted(set(filter(None, git("diff", "--name-only", "--no-renames", "-z", base, head).split("\0"))))
    mode = classify(paths, lambda path: (revision_file(base, path), revision_file(head, path)))
    deploy = deployment_changed(paths)
    # A fast PR must not let an earlier, still-unvalidated master change reach production.
    if event_name == "push" and deploy:
        mode = "full"
    return {
        "sha": sha,
        "mode": mode,
        "deploy": deploy,
        "infra": any(path.startswith("infra/") or path in POLICY_FILES for path in paths),
        "files": paths,
    }


def verify_deployment(decision, sha):
    if not re.fullmatch(r"[0-9a-f]{40}", sha) or decision["sha"] != sha:
        raise ValueError("CI artifact does not match the deployment commit.")
    if decision["mode"] not in {"docs", "landing", "full"}:
        raise ValueError("Invalid CI mode.")
    if type(decision["deploy"]) is not bool:
        raise ValueError("The deployment decision must be a boolean.")
    if decision["mode"] == "docs" and decision["deploy"]:
        raise ValueError("A documentation-only CI cannot authorize deployment.")
    return decision["deploy"]


def verify_current_deployment(sha, current_master):
    if not all(re.fullmatch(r"[0-9a-f]{40}", value) for value in (sha, current_master)):
        raise ValueError("Full Git commit SHAs are required.")
    paths = list(filter(None, git("diff", "--name-only", "--no-renames", "-z", sha, current_master).split("\0")))
    if deployment_changed(paths):
        raise ValueError("This automatic deployment is superseded by runtime changes on master; wait for their CI.")



def write_output(name, value):
    if "GITHUB_OUTPUT" in os.environ:
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
            output.write(f"{name}={str(value).lower() if isinstance(value, bool) else value}\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    subparsers = parser.add_subparsers(dest="command", required=True)
    select = subparsers.add_parser("classify")
    select.add_argument("--event", type=Path, required=True)
    select.add_argument("--event-name", required=True)
    select.add_argument("--sha", required=True)
    select.add_argument("--output", type=Path, required=True)
    verify = subparsers.add_parser("verify-deployment")
    verify.add_argument("decision", type=Path)
    verify.add_argument("--sha", required=True)
    current = subparsers.add_parser("check-current-deployment")
    current.add_argument("--sha", required=True)
    current.add_argument("--current-master", required=True)
    args = parser.parse_args()
    if args.command == "check-current-deployment":
        verify_current_deployment(args.sha, args.current_master)
        print("The tested app revision is current; intervening documentation changes do not require redeployment.")
        return
    if args.command == "verify-deployment":
        deploy = verify_deployment(json.loads(args.decision.read_text(encoding="utf-8")), args.sha)
        write_output("deploy", deploy)
        print(f"CI matches {args.sha}; deployment required: {deploy}.")
        return
    decision = impact(args.event_name, json.loads(args.event.read_text(encoding="utf-8")), args.sha)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(decision, indent=2) + "\n", encoding="utf-8")
    for key in ("mode", "deploy", "infra"):
        write_output(key, decision[key])
    summary = (
        f"CI scope: **{decision['mode']}** ({len(decision['files'])} changed paths). "
        f"Deployment required: **{decision['deploy']}**.\n"
        "Documentation uses lightweight checks; landing-only PRs use focused build/integration/browser checks; "
        "deployable master pushes and all other changes use the full suite. Gitleaks remains independent.\n"
    )
    print(summary)
    if "GITHUB_STEP_SUMMARY" in os.environ:
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as output:
            output.write(summary)


if __name__ == "__main__":
    main()
