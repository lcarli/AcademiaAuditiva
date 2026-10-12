import json
import os
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch
from xml.etree import ElementTree

import ci_scope


def resources(home="old", other="unchanged", extra="", header="same"):
    return (
        f'<root><resheader name="version"><value>{header}</value></resheader>'
        f'<data name="Home.Title"><value>{home}</value></data>'
        f'<data name="Exercise.Name"><value>{other}</value></data>{extra}</root>'
    )


class ScopeTests(unittest.TestCase):
    def classify(self, paths, before=None, after=None):
        return ci_scope.classify(paths, lambda _: (before, after))

    def test_documentation_and_nonruntime_illustrations_are_lightweight(self):
        for path in ["readme.md", "CONTRIBUTING.md", "LICENSE", "docs/guide.md", "docs/example.webp", "docs/manual.pdf"]:
            with self.subTest(path=path):
                self.assertEqual("docs", self.classify([path]))

    def test_extra_files_are_not_automatically_exempt(self):
        for path in ["extra.bin", "docs/tool.py", "scripts/run.ps1", ".dockerignore", "AcademiaAuditiva/Audio/new.wav"]:
            with self.subTest(path=path):
                self.assertEqual("full", self.classify([path]))

    def test_landing_and_documentation_use_focused_checks(self):
        paths = [
            "AcademiaAuditiva/Views/Home/Index.cshtml",
            "Tests/AcademiaAuditiva.IntegrationTests/HomePageTests.cs",
            "AcademiaAuditiva/wwwroot/img/landing/hero.webp",
            "docs/guide.md",
        ]
        self.assertEqual("landing", self.classify(paths))

    def test_home_resource_changes_are_focused_in_every_culture(self):
        for path in ci_scope.RESOURCE_FILES:
            with self.subTest(path=path):
                self.assertEqual("landing", self.classify([path], resources(), resources(home="new")))

    def test_home_key_additions_and_removals_are_focused(self):
        extended = resources(extra='<data name="Home.New"><value>new</value></data>')
        self.assertEqual("landing", self.classify([next(iter(ci_scope.RESOURCE_FILES))], resources(), extended))
        self.assertTrue(ci_scope.home_resources_only(extended, resources()))

    def test_global_resource_changes_require_full_checks(self):
        self.assertFalse(ci_scope.home_resources_only(resources(), resources(other="different")))
        self.assertFalse(ci_scope.home_resources_only(resources(), resources(header="different")))
        self.assertFalse(ci_scope.home_resources_only(resources(), resources().replace('name="Exercise.Name"', 'name="Exercise.Other"')))
        self.assertFalse(ci_scope.home_resources_only(resources(), resources().replace('<root>', '<root changed="true">')))

    def test_missing_or_new_resource_files_require_full_checks(self):
        self.assertFalse(ci_scope.home_resources_only(None, resources()))
        self.assertFalse(ci_scope.home_resources_only(resources(), None))

    def test_nonstring_resource_shapes_require_full_checks(self):
        for changed in [
            resources().replace('name="Home.Title"', 'name="Home.Title" type="System.Resources.ResXFileRef"'),
            resources().replace("<value>old</value>", "<value><nested>old</nested></value>"),
            resources().replace("<value>old</value>", "<value>old</value><comment>metadata</comment>"),
        ]:
            with self.subTest(changed=changed):
                self.assertFalse(ci_scope.home_resources_only(resources(), changed))

    def test_invalid_resources_fail_explicitly(self):
        with self.assertRaises(ElementTree.ParseError):
            ci_scope.home_resources_only(resources(), "<root>")
        with self.assertRaisesRegex(ValueError, "Duplicate"):
            ci_scope.home_resources_only(resources(), resources(extra='<data name="Home.Title"><value>duplicate</value></data>'))

    def test_unknown_cultures_and_shared_ui_require_full_checks(self):
        for path in [
            "AcademiaAuditiva/Resources/SharedResources.es.resx",
            "AcademiaAuditiva/Views/Shared/_Layout.cshtml",
            "AcademiaAuditiva/wwwroot/css/aa-design-system.css",
            "AcademiaAuditiva/wwwroot/js/core/audio-engine.js",
            "Tests/e2e/tests/academia-auditiva.spec.ts",
        ]:
            with self.subTest(path=path):
                self.assertEqual("full", self.classify([path]))

    def test_backend_dependencies_and_ci_policy_require_full_checks(self):
        for path in [
            "AcademiaAuditiva/Services/ExerciseCatalog.cs",
            "Directory.Packages.props",
            "global.json",
            ".github/workflows/ci.yml",
            ".github/workflows/cd.yml",
            *ci_scope.POLICY_FILES,
        ]:
            with self.subTest(path=path):
                self.assertEqual("full", self.classify(["AcademiaAuditiva/Views/Home/Index.cshtml", path]))

    def test_empty_diff_is_conservative(self):
        self.assertEqual("full", self.classify([]))

    def test_deployment_paths_preserve_existing_cd_policy(self):
        for path in [*ci_scope.DEPLOYMENT_FILES, "AcademiaAuditiva/Views/Home/Index.cshtml", "infra/main.bicep"]:
            with self.subTest(path=path):
                self.assertTrue(ci_scope.deployment_changed([path]))
        for path in ["docs/guide.md", "Tests/e2e/tests/academia-auditiva.spec.ts", ".github/workflows/ci.yml"]:
            with self.subTest(path=path):
                self.assertFalse(ci_scope.deployment_changed([path]))

    def test_manual_validation_is_full_and_authorizes_explicit_redeployment(self):
        for event in ["workflow_dispatch", "workflow_call"]:
            with self.subTest(event=event):
                self.assertEqual(
                    {"sha": "a" * 40, "mode": "full", "deploy": True, "infra": True, "files": []},
                    ci_scope.impact(event, {}, "a" * 40),
                )

    def test_scheduled_validation_does_not_deploy(self):
        self.assertFalse(ci_scope.impact("schedule", {}, "a" * 40)["deploy"])

    def test_unknown_events_and_invalid_shas_fail(self):
        with self.assertRaises(ValueError):
            ci_scope.impact("unknown", {}, "a" * 40)
        with self.assertRaises(ValueError):
            ci_scope.impact("push", {}, "invalid")

    def test_verify_requires_the_exact_tested_commit(self):
        for mode in ["landing", "full"]:
            with self.subTest(mode=mode):
                self.assertTrue(ci_scope.verify_deployment({"sha": "a" * 40, "mode": mode, "deploy": True}, "a" * 40))
        self.assertFalse(ci_scope.verify_deployment({"sha": "a" * 40, "mode": "docs", "deploy": False}, "a" * 40))
        with self.assertRaises(ValueError):
            ci_scope.verify_deployment({"sha": "a" * 40, "mode": "full", "deploy": True}, "b" * 40)

    def test_invalid_deployment_decisions_fail(self):
        for mode, deploy in [("invalid", True), ("docs", True), ("full", "true"), ("full", 1)]:
            with self.subTest(mode=mode, deploy=deploy), self.assertRaises(ValueError):
                ci_scope.verify_deployment({"sha": "a" * 40, "mode": mode, "deploy": deploy}, "a" * 40)


class GitImpactTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.previous = Path.cwd()
        os.chdir(self.root)
        self.addCleanup(os.chdir, self.previous)
        self.git("init", "--quiet")
        self.git("config", "user.name", "CI test")
        self.git("config", "user.email", "ci@example.test")
        self.git("config", "commit.gpgsign", "false")
        self.commit("docs/first.md", "first")

    def git(self, *args):
        return subprocess.run(["git", *args], check=True, capture_output=True, text=True).stdout.strip()

    def commit(self, path, content):
        file = self.root / path
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(content, encoding="utf-8")
        self.git("add", path)
        self.git("commit", "--quiet", "-m", "test change")
        return self.git("rev-parse", "HEAD")

    def test_push_accounts_for_all_commits_not_just_the_last(self):
        base = self.git("rev-parse", "HEAD")
        self.commit("AcademiaAuditiva/Services/Changed.cs", "backend change")
        head = self.commit("docs/last.md", "documentation")
        decision = ci_scope.impact("push", {"before": base}, head)
        self.assertEqual("full", decision["mode"])
        self.assertTrue(decision["deploy"])
        self.assertIn("AcademiaAuditiva/Services/Changed.cs", decision["files"])

    def test_pr_uses_merge_base_instead_of_unrelated_target_branch_changes(self):
        base = self.git("rev-parse", "HEAD")
        head = self.commit("AcademiaAuditiva/Views/Home/Index.cshtml", "landing change")
        self.git("checkout", "--quiet", "--detach", base)
        target = self.commit("AcademiaAuditiva/Services/Unrelated.cs", "target branch change")
        decision = ci_scope.impact("pull_request", {"pull_request": {"base": {"sha": target}, "head": {"sha": head}}}, "a" * 40)
        self.assertEqual("landing", decision["mode"])
        self.assertEqual(["AcademiaAuditiva/Views/Home/Index.cshtml"], decision["files"])

    def test_renaming_runtime_files_to_documentation_cannot_skip_full_checks(self):
        base = self.commit("AcademiaAuditiva/Services/Changed.cs", "same content")
        destination = self.root / "docs" / "moved.md"
        (self.root / "AcademiaAuditiva" / "Services" / "Changed.cs").rename(destination)
        self.git("add", "--all")
        self.git("commit", "--quiet", "-m", "rename")
        head = self.git("rev-parse", "HEAD")
        decision = ci_scope.impact("push", {"before": base}, head)
        self.assertEqual("full", decision["mode"])
        self.assertTrue(decision["deploy"])

    def test_resource_content_is_checked_from_the_actual_git_revisions(self):
        path = "AcademiaAuditiva/Resources/SharedResources.resx"
        base = self.commit(path, resources())
        head = self.commit(path, resources(home="new"))
        self.assertEqual("landing", ci_scope.impact("pull_request", {"pull_request": {"base": {"sha": base}, "head": {"sha": head}}}, head)["mode"])
        other = self.commit(path, resources(home="new", other="changed"))
        self.assertEqual("full", ci_scope.impact("pull_request", {"pull_request": {"base": {"sha": head}, "head": {"sha": other}}}, other)["mode"])

    def test_deployable_master_pushes_keep_full_validation(self):
        base = self.git("rev-parse", "HEAD")
        head = self.commit("AcademiaAuditiva/Views/Home/Index.cshtml", "landing change")
        self.assertEqual("full", ci_scope.impact("push", {"before": base}, head)["mode"])

    def test_documentation_does_not_supersede_a_tested_runtime_revision(self):
        tested = self.commit("AcademiaAuditiva/Views/Home/Index.cshtml", "landing")
        current = self.commit("docs/last.md", "documentation")
        ci_scope.verify_current_deployment(tested, current)
        ci_scope.verify_current_deployment(tested, tested)

    def test_superseded_runtime_revisions_cannot_be_automatically_redeployed(self):
        tested = self.commit("AcademiaAuditiva/Services/Changed.cs", "original")
        current = self.commit("AcademiaAuditiva/Services/Changed.cs", "newer")
        with self.assertRaisesRegex(ValueError, "superseded"):
            ci_scope.verify_current_deployment(tested, current)

    def test_current_deployment_cli_accepts_docs_but_rejects_newer_runtime_changes(self):
        tested = self.commit("AcademiaAuditiva/Services/Changed.cs", "original")
        documentation = self.commit("docs/last.md", "documentation")
        subprocess.run(
            [sys.executable, ci_scope.__file__, "check-current-deployment", "--sha", tested, "--current-master", documentation],
            check=True, capture_output=True, text=True,
        )
        current = self.commit("AcademiaAuditiva/Services/Changed.cs", "newer")
        result = subprocess.run(
            [sys.executable, ci_scope.__file__, "check-current-deployment", "--sha", tested, "--current-master", current],
            capture_output=True, text=True,
        )
        self.assertNotEqual(0, result.returncode)
        self.assertIn("superseded", result.stderr)

    def test_initial_push_is_full(self):
        head = self.git("rev-parse", "HEAD")
        decision = ci_scope.impact("push", {"before": "0" * 40}, head)
        self.assertEqual("full", decision["mode"])
        self.assertFalse(decision["deploy"])

    def test_invalid_git_revision_fails_instead_of_skipping_checks(self):
        with self.assertRaises(subprocess.CalledProcessError):
            ci_scope.impact("push", {"before": "b" * 40}, self.git("rev-parse", "HEAD"))

    def test_cli_persists_scope_and_github_outputs(self):
        base = self.git("rev-parse", "HEAD")
        head = self.commit("docs/second.md", "docs")
        event = self.root / "event.json"
        event.write_text(json.dumps({"before": base}), encoding="utf-8")
        output = self.root / "outputs"
        summary = self.root / "summary"
        with patch.dict(os.environ, {"GITHUB_OUTPUT": str(output), "GITHUB_STEP_SUMMARY": str(summary)}):
            subprocess.run(
                [
                    sys.executable, str(Path(ci_scope.__file__).resolve()), "classify",
                    "--event", str(event), "--event-name", "push", "--sha", head,
                    "--output", "artifacts/ci-impact.json",
                ], check=True, capture_output=True, text=True,
            )
        decision = json.loads((self.root / "artifacts" / "ci-impact.json").read_text(encoding="utf-8"))
        self.assertEqual("docs", decision["mode"])
        self.assertIn("mode=docs", output.read_text(encoding="utf-8"))
        self.assertIn("deploy=false", output.read_text(encoding="utf-8"))
        self.assertIn("**docs**", summary.read_text(encoding="utf-8"))

    def test_deployment_cli_publishes_only_verified_decisions(self):
        head = self.git("rev-parse", "HEAD")
        decision = self.root / "decision.json"
        output = self.root / "outputs"
        for mode, deploy in [("full", True), ("landing", True), ("docs", False)]:
            with self.subTest(mode=mode):
                decision.write_text(json.dumps({"sha": head, "mode": mode, "deploy": deploy}), encoding="utf-8")
                output.write_text("", encoding="utf-8")
                with patch.dict(os.environ, {"GITHUB_OUTPUT": str(output)}):
                    subprocess.run(
                        [sys.executable, ci_scope.__file__, "verify-deployment", str(decision), "--sha", head],
                        check=True, capture_output=True, text=True,
                    )
                self.assertEqual(f"deploy={str(deploy).lower()}\n", output.read_text(encoding="utf-8"))
        decision.write_text(json.dumps({"sha": head, "mode": "full", "deploy": True}), encoding="utf-8")
        output.write_text("", encoding="utf-8")
        with patch.dict(os.environ, {"GITHUB_OUTPUT": str(output)}):
            result = subprocess.run(
                [sys.executable, ci_scope.__file__, "verify-deployment", str(decision), "--sha", "b" * 40],
                capture_output=True, text=True,
            )
        self.assertNotEqual(0, result.returncode)
        self.assertEqual("", output.read_text(encoding="utf-8"))
        self.assertIn("does not match", result.stderr)


if __name__ == "__main__":
    unittest.main()
