# Badge art prompts

Prompts for generating the badge artwork with ChatGPT (issue #16). Each badge is a circular medal: a ring in the
group colour, a navy face and one bold emblem. The app shows badges at 88 px on the home page, at 64 px on the badges
page and in the celebration, and at 40 px on the dashboard, in light and dark themes, so the emblem must stay readable
at 40 px. Badges carry no words, because the site comes in three languages.
[Using the images in the app](#using-the-images-in-the-app) explains how an approved image gets into the app.

## How to use

1. Generate every badge in one ChatGPT conversation. If you start a new conversation, paste the style guide first.
2. Send badge 1 in the same message as the style guide. For every other badge, paste only its prompt and attach the
   reference badge the prompt names.
3. The first badge of each new colour becomes the reference for the rest of its group: badge 7 for violet, badge 12
   for green and badge 18 for magenta. Approve it before generating the other badges in that group.
4. Download the PNG and check it before moving on:
   - the background is truly transparent and the medal is centred, filling about 92% of the canvas;
   - the ring and the glow are in the group colour;
   - the emblem is still recognisable when shrunk to 40 px;
   - there is no text (except a numeral when the prompt asks for one) and nothing outside the circle.
5. If ChatGPT copies the reference's emblem (sun, staff line…), reply:
   "Keep the frame, but redo the emblem exactly as described."

## Status

All 18 badges players can earn are in the app. The file in brackets is the original PNG, kept outside the repository.

| # | Key | Title (pt-BR) | Group | Status |
|---|-----|---------------|-------|--------|
| 1 | `first_session` | Primeiras notas | Dedication | Approved, style reference (`Golden Sunrise Music Badge.png`) |
| 2 | `3_days` | 3 dias seguidos | Dedication | Approved (`Golden Flame Achievement Badge 3.png`) |
| 3 | `5_days` | 5 dias seguidos | Dedication | Approved (`Glossy Gold Flame Badge with Number 5.png`) |
| 4 | `marathon_20min` | Maratona de 20 minutos | Dedication | Approved (`Glossy Golden Stopwatch Badge.png`) |
| 5 | `faithful_practitioner` | Praticante fiel | Dedication | Approved, redone with the follow-up prompt below (`Golden Heart Metronome Badge.png`) |
| 6 | `10_sessions_week` | 10 sessões em uma semana | Dedication | Approved (`Golden Calendar Lightning Badge.png`) |
| 7 | `master_chords` | Mestre dos acordes | Mastery | Approved, violet reference (`Crowned Triple Note Badge.png`) |
| 8 | `sharp_listener` | Ouvido afiado | Mastery | Approved (`Ear Tuning Purple Badge.png`) |
| 9 | `rhythm_maestro` | Maestro do ritmo | Mastery | Approved (`Purple Snare Drum Achievement Badge.png`) |
| 10 | `melody_explorer` | Explorador melódico | Mastery | Approved, redone with the follow-up prompt below (`Glossy Purple Compass Music Badge.png`) |
| 11 | `scale_climber` | Escalador de tons | Mastery | Approved, redone with the follow-up prompt below (`Glossy Purple Musical Achievement Badge.png`) |
| 12 | `comeback_kid` | Deu a volta por cima | Progress | Approved, green reference (`Emerald Recovery Arrow Badge.png`) |
| 13 | `advanced_conqueror` | Conquistador avançado | Progress | Approved (`Emerald Trophy Music Badge.png`) |
| 14 | `persistent_student` | Aluno persistente | Progress | Approved (`Emerald Sprout Music Badge.png`) |
| 15 | `notable_progress` | Evolução notável | Progress | Approved (`Emerald Rocket Launch Badge.png`) |
| 16 | `resilient_ear` | Ouvido resiliente | Progress | Approved (`Emerald Shield Check Badge.png`) |
| 17 | `interval_tamer` | Domador de intervalos | Progress | Approved (`Emerald Musical Notes Badge.png`) |
| 18 | `badge_collector` | Colecionador de conquistas | Fun | Approved, magenta reference (`Gemstone Music Medal Badge.png`) |

The 15 [hidden badges](#hidden-badges) are at the end. Their rules are in the app; each one is released once its art
is approved.

## Style guide

Paste this first, together with badge 1.

```
Style guide for a set of achievement badges for "Academia Auditiva", an ear-training music app. Apply it to every badge in this conversation.

FORMAT
- One square image, 1024x1024 px, PNG with a truly transparent background (real alpha, no checkerboard, no backdrop).
- The badge is a perfect circular medal, centred, filling about 92% of the canvas width.

MEDAL FRAME (identical on every badge; only the group colour and the central emblem change)
- Thick outer ring (about 9% of the diameter) in the group colour: matte, lighter at the top-left, slightly deeper at the bottom-right.
- A thin off-white rim line just inside the ring.
- Inner face: deep navy #0B1A33 with a faint radial glow of the group colour behind the emblem.

EMBLEM
- One bold, simple symbol centred on the face, about 55% of the medal's diameter.
- Off-white #F4F6FB with accents in the group colour.
- Thick rounded shapes, at most 2-3 elements, so it stays readable when the whole badge is shrunk to 40x40 px.

GROUP COLOURS
- Dedication: amber gold (#FBBF24 to #B45309)
- Mastery: violet (#8F80FF to #6650FC)
- Progress: emerald green (#4ADE9B to #0B7A53)
- Fun: plum magenta (#BE79BF to #9B3D9C)

STYLE
Modern flat vector illustration with subtle gradients, clean geometric shapes, crisp edges and soft light from the top-left. Friendly and polished, like a premium mobile-app achievement.

NEVER
Letters, words or any text (a numeral only when a badge asks for one); photorealism, 3D render, glitter, tiny details, hairlines, ribbons, banners, stars or sparkles outside the medal, outer glow or drop shadow outside the circle.
```

## Dedication (amber)

### 1. `first_session`: Primeiras notas

```
Badge 1 - group: Dedication (amber ring).
Meaning: earned for answering the very first exercise, the start of a musical journey.
Emblem: two beamed eighth notes in off-white, standing on a short horizontal staff line. Behind them, a small amber rising sun (a half circle with 5 short, thick rays) peeks above the line, like dawn.
Do not write any text on the badge.
```

### 2. `3_days`: 3 dias seguidos

```
Badge 2 of 18 - group: Dedication (amber ring).
Attached: the approved badge 1. Use it ONLY as the style reference: same medal frame, ring thickness, thin off-white rim, navy face, soft glow, lighting, outline weight and level of detail. Do not reuse its emblem (no sun, no rays, no staff line, no notes).
Meaning: practised 3 days in a row (a 3-day streak).
Emblem: one bold, rounded flame with an amber-to-orange gradient and a large, light off-white inner flame. Inside the inner flame, a thick, rounded numeral "3" in deep navy #0B1A33, as big as the inner flame allows.
The numeral 3 is the only character on the badge. Transparent background.
```

### 3. `5_days`: 5 dias seguidos

```
Badge 3 of 18 - group: Dedication (amber ring).
Attached: the approved badge 1. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: practised 5 days in a row (a 5-day streak), the next step after the 3-day streak badge you just made.
Emblem: the same flame design as the 3-day badge, but more intense: taller and fuller (still about 60% of the medal), with a smaller flame tongue rising on each side and hotter orange-red tips. Keep the large off-white inner flame, with a thick, rounded numeral "5" in deep navy #0B1A33, as big as the inner flame allows.
The numeral 5 is the only character on the badge. Transparent background.
```

### 4. `marathon_20min`: Maratona de 20 minutos

```
Badge 4 of 18 - group: Dedication (amber ring).
Attached: the approved badge 1. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: practised for 20 minutes in a row without long breaks (a practice marathon).
Emblem: a bold, chunky off-white stopwatch seen from the front (round body, short crown button on top, small button on the upper right) with a light dial. On the dial, a thick amber filled wedge covers exactly one third, from 12 to 4 o'clock (20 of 60 minutes), and one thick navy hand points to 4 o'clock.
No tick marks, no numerals, no text.
```

### 5. `faithful_practitioner`: Praticante fiel

```
Badge 5 of 18 - group: Dedication (amber ring).
Attached: the approved badge 1. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: completed 30 practice sessions; a faithful, devoted student.
Emblem: a classic pyramid-shaped metronome in off-white, seen straight from the front and symmetrical, with a tall navy slot down its front. Inside the slot, an off-white pendulum arm tilted slightly to the right carries a bold amber heart as its sliding weight.
No scale markings, no numerals, no text.
```

The art in the app is a second version, because the first one's heart turned into a dot at 40 px. Attach the first
version and send:

```
Same badge, same composition and style, only two changes: make the amber heart about twice as large so it stays readable when the badge is shrunk to 40 px, and make the off-white walls of the metronome a bit thicker. Keep everything else identical. Transparent background.
```

### 6. `10_sessions_week`: 10 sessões em uma semana

```
Badge 6 of 18 - group: Dedication (amber ring).
Attached: the approved badge 1. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: completed 10 practice sessions in a single week (an intense week).
Emblem: a chunky off-white tear-off calendar page with two short binder rings on top, showing a thick, rounded numeral "10" in deep navy #0B1A33 in its upper middle. A bold amber lightning bolt overlaps the bottom-right corner of the page without covering the numeral.
The numeral 10 is the only text: no weekday or month names. Transparent background.
```

## Mastery (violet)

Badge 7 sets the violet ring. Once it is approved, attach it to badges 8–11.

### 7. `master_chords`: Mestre dos acordes

```
Badge 7 of 18 - group: Mastery. NEW GROUP COLOUR: the outer ring and the glow behind the emblem must be VIOLET (#8F80FF to #6650FC), not amber.
Attached: the approved badge 1. Use it ONLY as the style reference (frame shape, ring thickness, thin off-white rim, navy face, lighting, level of detail). Do not reuse its emblem or its amber colour.
Meaning: mastered the chord exercises (chord master).
Emblem: a three-note chord as in sheet music: three large off-white filled noteheads stacked in a vertical column, sharing one thick stem on the right side, with a small violet crown sitting slightly tilted on top of the stem.
No staff lines, no letters, no text. Transparent background.
```

### 8. `sharp_listener`: Ouvido afiado

```
Badge 8 of 18 - group: Mastery (violet ring).
Attached: the approved badge 7. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: a sharp ear: 18 of 20 answers right in a row in three ear-training exercises.
Emblem: a bold, simplified human ear in off-white seen from the side (a few thick rounded curves, like an app icon), with a large violet musical sharp sign ♯ (two upright strokes crossed by two thick, upward-slanted bars) overlapping its upper right.
No letters, no text. Transparent background.
```

### 9. `rhythm_maestro`: Maestro do ritmo

```
Badge 9 of 18 - group: Mastery (violet ring).
Attached: the approved badge 7. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: rhythm mastery: two rhythm sessions without a single mistake.
Emblem: a chunky snare drum in flat front view (off-white shell, violet top and bottom hoops, a simple violet zigzag of tension cords) with two thick off-white drumsticks crossed in an X above it.
No text. Transparent background.
```

### 10. `melody_explorer`: Explorador melódico

```
Badge 10 of 18 - group: Mastery (violet ring).
Attached: the approved badge 7. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: explored every melody exercise, with 8 of 10 answers right in a row in each one.
Emblem: a chunky off-white pocket compass seen from the front (round case, small ring on top) with a navy dial. On the dial, a bold diamond-shaped needle, its violet half pointing to the upper right and its off-white half to the lower left, and a small off-white eighth note in place of the north mark at the top of the dial.
No letters (no N, S, E, W), no numerals, no text. Transparent background.
```

The art in the app is a second version, because at 40 px the first one's small needle and top ring made it look like
a pocket watch. Attach the first version and send:

```
Same badge, same frame and style, only three changes: make the diamond needle much larger, so that it fills most of the dial and clearly reads as a compass needle; make the off-white case thinner so that the dial gets bigger; and remove the small ring on top of the case, so that it no longer looks like a stopwatch. Keep the small eighth note at the top of the dial and everything else identical. Transparent background.
```

### 11. `scale_climber`: Escalador de tons

```
Badge 11 of 18 - group: Mastery (violet ring).
Attached: the approved badge 7. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: climbed every scale: 5 right answers in each scale exercise.
Emblem: a staircase of five chunky off-white steps rising from the lower left to the upper right, one round violet notehead resting on each step, and a small violet pennant flag planted on the top step.
No text. Transparent background.
```

The art in the app is a second version, because at 40 px the first one's thin steps and small noteheads blurred
together. Attach the first version and send:

```
Same badge, same frame and style. Make the staircase one solid, chunky off-white block of four tall steps rising from the lower left to the upper right (filling about 60% of the medal), with a larger round violet notehead on each step and a larger violet pennant flag on the top step. Keep everything else identical. Transparent background.
```

## Progress (green)

Badge 12 sets the green ring. Once it is approved, attach it to badges 13–17.

### 12. `comeback_kid`: Deu a volta por cima

```
Badge 12 of 18 - group: Progress. NEW GROUP COLOUR: the outer ring and the glow behind the emblem must be EMERALD GREEN (#4ADE9B to #0B7A53), not amber or violet.
Attached: the approved badge 1. Use it ONLY as the style reference (frame shape, ring thickness, thin off-white rim, navy face, lighting, level of detail). Do not reuse its emblem or its amber colour.
Meaning: a comeback: a bad start (3 misses in the first 5 answers), then 8 of 10 right in a row.
Emblem: one thick, rounded arrow that starts at the upper left, dives down into a deep curve and then shoots up steeply to the upper right, ending in a big arrowhead. The falling half is off-white and the rising half is green.
No text. Transparent background.
```

### 13. `advanced_conqueror`: Conquistador avançado

```
Badge 13 of 18 - group: Progress (green ring).
Attached: the approved badge 12. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: conquered the advanced level: 7 of 10 right in a row in five advanced exercises.
Emblem: a chunky off-white trophy cup with two rounded handles on a short stepped base, with a bold green eighth note on the front of the cup.
No text. Transparent background.
```

### 14. `persistent_student`: Aluno persistente

```
Badge 14 of 18 - group: Progress (green ring).
Attached: the approved badge 12. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: steady growth: accuracy went up in three sessions in a row.
Emblem: a large off-white quarter note (round notehead, thick stem) whose stem grows into a seedling: two bold green leaves sprouting from the top of the stem.
No text. Transparent background.
```

### 15. `notable_progress`: Evolução notável

```
Badge 15 of 18 - group: Progress (green ring).
Attached: the approved badge 12. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: remarkable progress: better results than in the previous 30 days in every category practised.
Emblem: a chunky off-white rocket taking off diagonally towards the upper right, with a round green window, green fins and a short, rounded off-white exhaust cloud behind it.
No stars, no text. Transparent background.
```

### 16. `resilient_ear`: Ouvido resiliente

```
Badge 16 of 18 - group: Progress (green ring).
Attached: the approved badge 12. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: resilience: got the answer right straight after three misses in a row.
Emblem: a sturdy off-white heater shield seen from the front, with a bold green check mark across its face.
No text. Transparent background.
```

### 17. `interval_tamer`: Domador de intervalos

```
Badge 17 of 18 - group: Progress (green ring).
Attached: the approved badge 12. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: tamed the intervals: ten interval sessions with at least 80% right.
Emblem: two bold off-white quarter notes, a low one on the left and a high one on the right (a wide interval), joined by a thick green lasso rope that rises from the low note and loops tightly around the head of the high note.
No staff lines, no text. Transparent background.
```

## Fun (magenta)

Badge 18 sets the magenta ring. Once it is approved, attach it to the other magenta badges below.

### 18. `badge_collector`: Colecionador de conquistas

```
Badge 18 of 18 - group: Fun. NEW GROUP COLOUR: the outer ring and the glow behind the emblem must be PLUM MAGENTA (#BE79BF to #9B3D9C), not amber, violet or green.
Attached: the approved badge 1. Use it ONLY as the style reference (frame shape, ring thickness, thin off-white rim, navy face, lighting, level of detail). Do not reuse its emblem or its amber colour.
Meaning: a collector: earned 15 other badges.
Emblem: a big, chunky faceted gem (classic diamond cut, seen from the front) in magenta with off-white highlights on its top facets, and a small off-white eighth note sparkling at its upper right.
No text. Transparent background.
```

## Hidden badges

These 15 badges are in the app with their rules, tests and texts in the three languages, but `BadgeCatalog` keeps
them hidden (`IsAvailable: false`) until their art is ready: no one earns them and players don't see them. Badges are
awarded from the player's whole history, so once a badge is released, everyone who already meets its rule gets it with
their next answer.

To release a badge:

1. Generate its art with the prompt below and export it (see [Using the images in the app](#using-the-images-in-the-app)).
2. Remove its `IsAvailable: false` in `AcademiaAuditiva/Services/Gamification/BadgeCatalog.cs`.

When they are all out, raise `BadgeRules.BadgeCollectorThreshold` from 15 to 20, and change the 15 in the
`Badge.badge_collector.Description` texts (3 resx files), in its seed row (`SeedData.cs`) and in the badge 18 prompt.

| Key | Title (pt-BR) | Group | Rule (`BadgeRules.cs`) |
|-----|---------------|-------|------------------------|
| `7_days` | 7 dias seguidos | Dedication | Best streak of at least 7 days |
| `30_days` | 30 dias seguidos | Dedication | Best streak of at least 30 days |
| `100_sessions` | Disco de ouro | Dedication | 100 sessions |
| `daily_challenge_complete` | Desafio do dia completo | Dedication | A daily challenge completed |
| `explorer` | Explorador | Dedication | An answer in every exercise except Solfege Melody, which needs a microphone |
| `filter_ninja` | Filtro ninja | Dedication | 5 sessions with an answer played with some filter on another option than its first one |
| `perfect_session` | Sessão perfeita | Mastery | A session of at least 10 answers, all right |
| `total_mastery` | Domínio total | Progress | Every step of the learning path completed |
| `mission_addict` | Viciado em desafios | Fun | 10 daily challenges completed |
| `speedster` | Velocista | Fun | 18 of 20 answers in a row right, at most 4 minutes from the first to the last |
| `mystery_listener` | Ouvinte misterioso | Fun | 5 answers in a row right in Guess Note |
| `impossible_melody` | Melodia impossível | Fun | 3 answers in a row right in Melodic Dictation |
| `all_rounder` | Músico completo | Fun | At least 10 answers in every exercise category (Harmony, Melody, Rhythm, EarTraining, Scales) |
| `night_owl` | Coruja da noite | Fun | A session started between 22:00 and 04:59, local time |
| `early_bird` | Madrugador | Fun | A session started between 05:00 and 06:59, local time |

A session is a run of at least 5 answers with no pause longer than 30 minutes, across all exercises. The filters come
from the answers (`ScoreSnapshot.FilterJson`); answers saved before #96 have none and never count. The completed daily
challenges are worked out from the answers with the same draw as the dashboard (`DailyChallengeRules.CompletedDays`).

### `7_days`: 7 dias seguidos

```
Extra badge - 7_days - group: Dedication (amber ring).
Attached: the approved 5-day streak badge (badge 3). Keep its frame, colours, lighting and flame style.
Meaning: practised 7 days in a row (a full week).
Emblem: the same flame, one step more intense than the 5-day one: wider, with two flame tongues rising on each side, and the off-white inner flame holding a thick, rounded numeral "7" in deep navy #0B1A33.
The numeral 7 is the only character on the badge. Transparent background.
```

### `30_days`: 30 dias seguidos

```
Extra badge - 30_days - group: Dedication (amber ring).
Attached: the approved 5-day streak badge (badge 3). Keep its frame, colours, lighting and flame style.
Meaning: practised 30 days in a row, the longest streak.
Emblem: the most intense version of the same flame, with a small off-white crown resting on its tip; the off-white inner flame holds a thick, rounded numeral "30" in deep navy #0B1A33.
The numeral 30 is the only text on the badge. Transparent background.
```

### `100_sessions`: Disco de ouro

```
Extra badge - 100_sessions - group: Dedication (amber ring).
Attached: the approved badge 1. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: completed 100 practice sessions, like a musician receiving a gold record.
Emblem: a gold-record award plaque seen from the front: a chunky off-white rectangular frame holding a big amber vinyl record with two or three thick groove bands and an off-white centre label with a small navy eighth note.
No text, no plaque inscription. Transparent background.
```

### `daily_challenge_complete`: Desafio do dia completo

```
Extra badge - daily_challenge_complete - group: Dedication (amber ring).
Attached: the approved badge 1. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: completed a daily challenge: the three exercises picked for the day.
Emblem: an off-white clipboard with an amber clip at the top and three rows, each ticked with a bold amber check mark.
No writing on the clipboard, no text. Transparent background.
```

### `explorer`: Explorador

```
Extra badge - explorer - group: Dedication (amber ring).
Attached: the approved badge 1. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: an explorer: answered every exercise at least once.
Emblem: a pair of chunky off-white binoculars seen from the front, with two big round amber lenses.
No text. Transparent background.
```

### `filter_ninja`: Filtro ninja

```
Extra badge - filter_ninja - group: Dedication (amber ring).
Attached: the approved badge 1. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: a filter ninja: practised with custom exercise filters in 5 sessions.
Emblem: a chunky off-white funnel (the classic filter symbol) wearing an amber ninja headband tied at the side, with its two ribbon tails flying to the right.
No text. Transparent background.
```

### `perfect_session`: Sessão perfeita

```
Extra badge - perfect_session - group: Mastery (violet ring).
Attached: the approved badge 7. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: a flawless session: at least 10 answers, all of them right.
Emblem: a bold off-white tuning fork standing upright in the centre, with two thick violet vibration arcs on each side of its prongs.
No text. Transparent background.
```

### `total_mastery`: Domínio total

```
Extra badge - total_mastery - group: Progress (green ring).
Attached: the approved badge 12. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: total mastery: completed every step of the learning path.
Emblem: a big, bold five-pointed star, off-white with soft green facets, framed by two green laurel branches curving around its lower half.
No text. Transparent background.
```

### `mission_addict`: Viciado em desafios

```
Extra badge - mission_addict - group: Fun (magenta ring).
Attached: the approved badge 18. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: hooked on challenges: completed 10 daily challenges.
Emblem: a round target of three thick concentric rings alternating off-white and magenta, with an off-white dart stuck in the bullseye.
No text. Transparent background.
```

### `speedster`: Velocista

```
Extra badge - speedster - group: Fun (magenta ring).
Attached: the approved badge 18. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: a lightning-fast ear: 18 of 20 answers right in a row, in under 4 minutes.
Emblem: an off-white eighth note flying to the right, with a pair of small magenta wings on its stem and three bold magenta speed streaks trailing behind it.
No text. Transparent background.
```

### `mystery_listener`: Ouvinte misterioso

```
Extra badge - mystery_listener - group: Fun (magenta ring).
Attached: the approved badge 18. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: a mystery listener: named 5 notes in a row just by hearing them.
Emblem: a chunky off-white mystery box with its lid popping open and a big magenta question mark rising out of it.
The question mark is the only symbol: no letters, no text. Transparent background.
```

### `impossible_melody`: Melodia impossível

```
Extra badge - impossible_melody - group: Fun (magenta ring).
Attached: the approved badge 18. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: wrote down 3 melodies in a row by ear without a single mistake (melodic dictation).
Emblem: an impossible (Penrose) triangle built from thick off-white and magenta bars, with a small off-white eighth note in its centre.
No text. Transparent background.
```

### `all_rounder`: Músico completo

```
Extra badge - all_rounder - group: Fun (magenta ring).
Attached: the approved badge 18. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: an all-round musician: at least 10 answers in every category (harmony, melody, rhythm, ear training and scales).
Emblem: a pentagon-shaped radar chart with five axes, completely filled in magenta, with a thick off-white outline, off-white dots at its five corners and a small off-white eighth note in the centre.
No labels, no text. Transparent background.
```

### `night_owl`: Coruja da noite

```
Extra badge - night_owl - group: Fun (magenta ring).
Attached: the approved badge 18. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: started practising late at night, between 10 p.m. and 5 a.m.
Emblem: a round, friendly off-white owl seen from the front, with big navy eyes, wearing magenta headphones, and a small off-white crescent moon at its upper right.
No text. Transparent background.
```

### `early_bird`: Madrugador

```
Extra badge - early_bird - group: Fun (magenta ring).
Attached: the approved badge 18. Use it ONLY as the style reference (frame, ring, rim, navy face, glow, lighting, level of detail). Do not reuse its emblem.
Meaning: started practising early in the morning, between 5 and 7 a.m.
Emblem: a small, round off-white songbird in profile, singing with its beak open, and a magenta eighth note floating out of its beak.
No sun, no text. Transparent background.
```

## Using the images in the app

The app shows each badge from `AcademiaAuditiva/wwwroot/img/badges/{key}.webp`, a 192 px export that stays sharp at
64 px on high-density screens. The original PNGs stay out of the repository.

To add or replace a badge, export its approved PNG and open a pull request with the new `.webp`:

```
python scripts/export-badge-art.py scale_climber "C:\path\to\Glossy Purple Musical Achievement Badge.png"
```

The script needs Pillow (`pip install pillow`). It checks that the key exists in `BadgeCatalog`, clears the faint noise
outside the medal's circle, crops the image to the medal and writes the WebP (quality 90). It also prints how much of
the PNG the medal fills, which should be about 92%.

- `Url.BadgeImage(key)` (`AcademiaAuditiva/Services/Gamification/BadgeDisplay.cs`) adds a hash of the file to the
  image URL, so browsers fetch a replaced image at once. The badges page (`Views/Shared/_BadgeMedal.cshtml`), the
  dashboard (`Views/Dashboard/Index.cshtml`), the home page (`Views/Home/Index.cshtml`, which shows the six badges of
  `BadgeCatalog.Showcase`) and the celebration after an answer (`ExerciseController.BuildRewards` sends the URL to
  `wwwroot/js/core/rewards.js`) all use it.
- The images have `alt=""`, since the badge title is always shown or read next to them.
- The locked state comes from CSS (grayscale and lower opacity), so no locked artwork is needed.
- `BadgeArtTests` fails if a badge players can earn has no image, or if an image is not named after a badge. A hidden
  badge can get its art first and be released later.
