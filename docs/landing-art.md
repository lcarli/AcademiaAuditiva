# Landing page art prompts

Prompts for generating the home page illustrations with ChatGPT (#101). The home page gets eight images in the style
of the badge medals ([badges.md](../badges.md)): three wide scenes with the student and the teacher, and five square
spot illustrations of musical objects. The page shows each image on a soft violet shape. That shape sits on a white
page in the light theme and on a navy page in the dark theme, so the art must stand out on both. The images carry no
words, because the site comes in three languages. [Using the images in the app](#using-the-images-in-the-app)
explains how an approved image gets into the app.

## How to use

1. Generate every image in one ChatGPT conversation. If you start a new conversation, paste the style guide first.
2. Send image 1 in the same message as the style guide. Attach two medals from `AcademiaAuditiva/wwwroot/img/badges`
   as a style reference: `master_chords.webp` and `comeback_kid.webp`, which have no letters or digits. Approve
   image 1 before going on, because it sets the style and the student for every other image.
3. For every other image, paste only its prompt and attach image 1.
4. Download the PNG and check it before moving on:
   - the background is truly transparent, with no checkerboard, floor, shadow or frame;
   - there are no letters, words or numbers anywhere, including on screens and clothes (the question mark of image 7
     is the only symbol of that kind);
   - faces and hands have no defects, and the student looks the same as in image 1;
   - nothing is cut off at the edges.
5. Preview the PNG on both themes:
   1. Run `python scripts/export-landing-art.py <name> <png> --preview`. With `--preview`, it does not change the
      app.
   2. Open the `-preview.png` the script writes next to your PNG. It shows the art in the light theme (top) and the
      dark theme (bottom), at full size and at about the smallest size the page shows it.
   3. Check that every part of the outline stands out in both themes and that the small copy is still clear.
6. If ChatGPT gets something wrong, reply with one of these:
   - "Keep everything, but make the background fully transparent: no checkerboard, no floor, no shadow."
   - "Keep everything, but remove every letter, number and word, including on the screen."
   - "Same character as the attached image: same face, skin, hair, headphones and clothes."
   - "Keep everything, but simplify the hands into rounded shapes with no finger detail."
   - "Keep everything, but make the [part] lighter: it disappears on a dark navy background."

## Status

None of the images is in the app yet. Once an image is approved, record its original PNG, which stays outside the
repository.

| # | Name | Where it goes on the home page | Canvas | Status |
|---|------|--------------------------------|--------|--------|
| 1 | `hero` | Hero, right of "Tune your ear.", in place of the staff | 1536×1024 | To do; reference for the style and the student |
| 2 | `student` | "Train on your own or with your class.", For students tab, beside the list | 1536×1024 | To do |
| 3 | `teacher` | Same section, For teachers tab, beside the list | 1536×1024 | To do |
| 4 | `step-1` | "Listen, answer, repeat.", above "Pick an exercise" | 1024×1024 | To do |
| 5 | `step-2` | Same section, above "Listen and answer" | 1024×1024 | To do |
| 6 | `step-3` | Same section, above "Follow your progress" | 1024×1024 | To do |
| 7 | `faq` | "Common questions", under the title, on wide screens only | 1024×1024 | To do |
| 8 | `final` | "Ready to train your ear?", above the title | 1024×1024 | To do |

## Style guide

Paste this first, together with image 1 and the two medals.

```
Style guide for the illustrations of the home page of "Academia Auditiva", an ear-training music app. Apply it to every image in this conversation.

FORMAT
- PNG with a truly transparent background: real alpha, no checkerboard, no backdrop, no floor, no frame.
- Wide scenes are 1536x1024 (3:2 landscape); spot illustrations are 1024x1024. Each prompt says which.
- The art is centred and fills about 85% of the canvas, with nothing cut off at the edges.

STYLE
Modern flat vector illustration with subtle gradients, clean rounded geometric shapes, crisp edges and soft light from the top-left. Friendly and polished, like a premium mobile app. It matches the app's achievement medals (amber, violet, emerald and magenta medals with a navy face), without their circular frame.

COLOURS
- Main colour: violet (#8F80FF to #6650FC), with pale violet (#C9C2FF) for light tints.
- Accents, used sparingly: amber gold (#FBBF24 to #B45309), emerald green (#4ADE9B to #0B7A53), plum magenta (#BE79BF to #9B3D9C) and teal (#0594AB).
- Navy (#0B1A33) and off-white (#F4F6FB) only for small details.
- The page shows each image on a pale lilac background in the light theme and on a dark navy background in the dark theme, so the outer edge of every shape must contrast with both: no large navy, black, white or off-white areas on the outline. Dark shapes such as hair get soft violet highlights so they stay visible on navy.

CHARACTERS (keep them identical in every image where they appear)
- The student: a young woman about 20 years old, warm brown skin, dark curly hair in a high puff, violet over-ear headphones, mustard-yellow hoodie, mid-blue jeans, violet-and-white sneakers.
- The teacher: a man about 45 years old, light olive skin, short salt-and-pepper hair and beard, round glasses, teal cardigan over a white shirt, grey trousers.
- Simple, friendly faces: dot eyes, a small smile, no nose detail. Simplified hands with no finger detail. Natural, slightly stylised proportions.

MUSIC
- Notes are simple noteheads with stems, beams or flags, floating freely. Keyboards are short and decorative. Sound waves are thick rounded arcs.
- Phones, tablets and laptops show only abstract shapes on their screens: coloured blocks, bars and dots.

NEVER
Letters, words, numbers or any text, also on screens, clothes and objects; logos or brand marks; readable sheet music; photorealism, 3D render, glitter, tiny details, hairlines; shadows or glows outside the art; a background, floor, frame or vignette.
```

## Wide scenes

### 1. `hero`

```
Image 1 of 8: hero. A wide scene, 1536x1024 (3:2 landscape), transparent background. Attached: two of the app's medals, as a style reference only; do not draw medals.

Waist-up view of the student, turned slightly to the right, smiling with her eyes half closed as she enjoys what she hears. She holds a phone in front of her at chest height with the screen facing her, so we see only its violet back. Thick rounded violet sound-wave arcs open out from her headphones to the right, and a stream of floating notes (a beamed pair of eighth notes and two quarter notes, one of them amber) drifts up and to the right. Three loose piano keys (two white, one black) float around her at playful angles. The student fills the left half of the image; the notes, waves and keys fill the right half.
```

### 2. `student`

```
Image 2 of 8: student. A wide scene, 1536x1024 (3:2 landscape), transparent background. Attached: image 1.

The same student as in the attached image: same face, skin, hair, headphones and hoodie. Full-body view: she sits comfortably on a big round violet beanbag, practising on her phone held in both hands, headphones on. At the upper right, a large rounded emerald-green speech bubble holds a bold off-white check mark: she just got the answer right. Two or three small notes float around her. Same style as the attached image.
```

### 3. `teacher`

```
Image 3 of 8: teacher. A wide scene, 1536x1024 (3:2 landscape), transparent background. Attached: image 1.

On the right, the teacher from the style guide stands smiling and holds a tablet turned towards the viewer. Its screen shows only three rounded rising bars: violet, emerald and amber. On the left, two students sit on simple round stools facing him, headphones on, seen in three-quarter view: the student from the attached image (same face, skin, hair, headphones and hoodie) and a young man with light skin, short black hair, teal headphones and a violet sweater. A few small notes float between them. Same style as the attached image.
```

## Spot illustrations

The steps show these at about 140 px wide, so keep them bold and simple.

### 4. `step-1`

```
Image 4 of 8: step 1, pick an exercise. A square spot illustration, 1024x1024, transparent background. Attached: image 1.

Three rounded cards float in a loose fan, slightly overlapping. Each shows one bold symbol: a single note on the left card, two notes side by side on the middle card, three stacked notes (a chord) on the right card. The middle card is violet with off-white notes and sits a little higher than the others, as if just picked; the side cards are pale violet with violet notes. No people. Same style as the attached image, and still clear at 140 px wide.
```

### 5. `step-2`

```
Image 5 of 8: step 2, listen and answer. A square spot illustration, 1024x1024, transparent background. Attached: image 1.

Violet over-ear headphones, the same model as the student's, float in the upper half with thick rounded sound-wave arcs on both sides. Below them, a short piano keyboard (eight white keys and five black keys), seen slightly from above, has one white key pressed down and glowing violet. No people. Same style as the attached image, and still clear at 140 px wide.
```

### 6. `step-3`

```
Image 6 of 8: step 3, follow your progress. A square spot illustration, 1024x1024, transparent background. Attached: image 1.

A bar chart of four thick rounded bars rising from left to right: pale violet, pale violet, violet, then emerald for the tallest. A bold amber star floats just above the tallest bar. No axes, gridlines, labels or numbers. No people. Same style as the attached image, and still clear at 140 px wide.
```

### 7. `faq`

```
Image 7 of 8: common questions. A square spot illustration, 1024x1024, transparent background. Attached: image 1.

A large rounded violet speech bubble with a short tail at the bottom left. Inside it, a bold off-white question mark whose dot is a musical notehead: a slightly tilted oval. Two small notes float near the bubble. No people. Same style as the attached image.
```

### 8. `final`

```
Image 8 of 8: tune your ear. A square spot illustration, 1024x1024, transparent background. Attached: image 1.

A tuning fork stands upright, slightly tilted, in silver-grey metal (#B4BDD0 shading to #53607A) with soft violet reflections. It is vibrating: thick rounded violet sound-wave arcs ring out on both sides of its prongs, and a few notes rise from it, one of them amber. No people. Same style as the attached image, and still clear at 140 px wide.
```

## Using the images in the app

The eight images go into the home page together, once all of them are approved (#101):

1. Export each approved PNG with `python scripts/export-landing-art.py <name> <png>` (Python 3.9+ and Pillow 9.1+).
   The script:
   - checks that the background is transparent;
   - crops the art and centres it with a small margin;
   - writes `AcademiaAuditiva/wwwroot/img/landing/<name>.webp`, at 1200×800 for the wide scenes and 480×480 for the
     square ones (twice their largest size on screen);
   - warns when the art touches the edge of the PNG, or is so small in it that enlarging it would blur it.
2. Add the images to `AcademiaAuditiva/Views/Home/Index.cshtml` in the places the [status](#status) table lists:
   - draw each one on an ellipse in `--aa-accent-soft`, like the preview;
   - give each `<img>` its `width` and `height`, so the page does not shift while it loads;
   - use `alt=""`, because the text beside each image already says the same thing;
   - use `loading="lazy"` on every image except the hero;
   - add `asp-append-version`, so browsers fetch a new export right away.
3. Run `HomePageTests`, which checks that every image on the home page is served as WebP. Then check the page in the
   light and dark themes and on a phone.

To replace an image later, export its new PNG over the old file.
