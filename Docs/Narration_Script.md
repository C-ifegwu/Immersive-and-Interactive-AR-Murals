# Murals in Motion: Video Narration Script

Two videos: a **demonstration** (about 7 minutes, filmed on the phone at the real murals) and a **technical walkthrough** (about 5 minutes, screen-recorded on the PC).

Read the **SAY** lines out loud. **SHOW** says what should be on screen. Speak slowly and pause on every effect: the effect is the point, the words just frame it.

---

## Video 1: Demonstration (about 7 min)

Film with a screen recorder on the phone, and if you can, a second phone filming you holding it in front of the wall.

### 0:00 Intro (30 s)
**SHOW:** You in front of the first mural, phone in hand.
**SAY:** "This is Murals in Motion, an augmented reality app that brings five murals on the ALU campus to life. You point your phone at a mural and the painting itself starts to move. Everything that moves is cut from the mural's own paint, so it's the artwork coming alive, not a 3D model placed on top."

### 0:30 The app (45 s)
**SHOW:** Open the app. Splash screen, tap Start, show the map, tap a pin, show the card.
**SAY:** "The app opens on a splash screen and then a map of campus. Each pin is a mural. Tap one and you get a photo, where to find it and a line of its story. The counter at the top tracks how many you've found, and pins turn green once you've scanned them. I'll tap Scan this mural."

### 1:15 M1, The Stone Head (60 s)
**SHOW:** Scan the stone head. Let the full reveal play. Walk slowly left and right. Tap the face.
**SAY:** "First, the stone head. Every mural starts the same way: the wall dims, so whatever moves next reads as coming out of the paint. The head lifts slightly off the wall and opens its eyes. Eyes that were never painted.
Now watch as I move. Its eyes follow me. And if I tap the face, it winks and tells me its story. This mural is about how old Africa's history is, and here that history looks back at you."

### 2:15 M2, The Caravan (60 s)
**SHOW:** Tap < SCAN, go to the desert mural. Let the camels start walking. Tap the caravan, then drag.
**SAY:** "I tap Scan to go back and find the next one. This is the desert. The camel caravan lifts off the dunes and starts walking, with sand drifting across. If I tap the caravan it stops to rest and I get the story of the trade routes it stands for. If I drag across the screen, the wind picks up. The sand blows harder and the camels hurry along."

### 3:15 M3, Convention Centre (60 s)
**SHOW:** Scan the dome. Let it go dark and light up. Tap the dome, then drag.
**SAY:** "This is my mural, the Kigali Convention Centre. The dome goes dark with the wall, then lights up band by band from the ground, the way it lights up the city at night, in Rwanda's blue, yellow and green. Riding round its base is the Tour du Rwanda. Tap the dome for a light show and its story. Drag, and the riders sprint."

### 4:15 M4, The Floating Figure (60 s)
**SHOW:** Scan the figure. Let the pieces float out. Tap a bangle, then drag.
**SAY:** "The floating figure, in traditional attire with gold ornaments. The bangles and cubes lift out of the painting and float in the air in front of it. Tap one and it spins. Drag, and they swing round the figure toward you. It's heritage and the future in one image, and here you can stand inside it."

### 5:15 M5, Living Paint (60 s)
**SHOW:** Scan the lady. Wait for the earrings to swing and the glint. Tap an earring, then drag.
**SAY:** "The last one is living paint. Nothing leaves the wall here; the painting itself moves. Her gold earrings swing, a glint of light runs down her neck rings, and the acacia trees sway. Tap an earring and it swings hard, then settles. Drag, and a gust of wind bends the trees and swings both earrings."

### 6:15 Tracking and finish (40 s)
**SHOW:** Point away from a mural mid-effect, then back. Then tap DONE, show the completion screen, BACK TO MAP, the map with green pins.
**SAY:** "If I look away, the content hides, and when I look back it carries on where it left off. It doesn't restart. When I'm done I tap Done, and I'm back at the map with every mural I've found in green."

### 6:55 Close (15 s)
**SAY:** "That's Murals in Motion: five murals, five ways for a painting to come alive, all in one app. Thank you."

---

## Video 2: Technical walkthrough (about 5 min)

Screen-record the PC: Unity, the project window and the code.

### 0:00 Setup (40 s)
**SHOW:** Unity with Main.unity open. Point at the XR Origin in the hierarchy.
**SAY:** "The app is built in Unity 6 with the Universal Render Pipeline and AR Foundation, using Google ARCore on Android. Image tracking runs on the XR Origin through the AR Tracked Image Manager, with one reference library holding all five murals, named M1 to M5."

### 0:40 How a mural is found (60 s)
**SHOW:** Open ARMuralManager.cs.
**SAY:** "The only shared runtime script is the AR Mural Manager. When ARCore detects an image it matches the name to a mural experience and parents the content to the tracked image, so it sits on the real wall. It also scales the content to the size ARCore measures, so it fits whatever it's looking at. If tracking drops for more than half a second, the content hides and the state is kept, so it resumes instead of restarting."

### 1:40 One contract for every mural (60 s)
**SHOW:** Open MuralExperienceBase.cs, then LayeredMuralExperience.cs.
**SAY:** "Every mural implements the same three methods: tracking found, tracking lost and reset. On top of that, a shared Layered Mural Experience class does what all five have in common: it dims the painted wall first, reads taps and drags, opens the story, and resumes after tracking loss. Each mural only writes its reveal, its live animation and its two interactions."

### 2:40 Built from the paint (60 s)
**SHOW:** The Textures folder of a mural, e.g. the M3 dome bands or the M5 earrings, then a prefab in the Scene view.
**SAY:** "Every moving part is cut out of the mural's own photograph, using a soft-edged mask drawn round its outline or picked out by colour. Each cut-out sits exactly over its paint, so at first nothing looks different. Then it moves. That's why the effects look like the artwork moving, not something stuck on top."

### 3:40 One example in code (50 s)
**SHOW:** Open M5LivingPaintExperience.cs and scroll to Animate().
**SAY:** "As an example, M5's earrings are a damped pendulum: each frame the swing is pulled back to centre, slowed by damping and pushed by the breeze and by the user's wind. A tap adds a kick. The other murals work the same way: blinking timers, a walk cycle, a light chase, or floating and orbiting."

### 4:30 UI and close (30 s)
**SHOW:** Play mode: splash, map, card, scanning screen.
**SAY:** "The interface is one shared set of screens driven by a UI shell controller, with the campus map built from a single data asset of titles, levels and stories. That's the technical side of Murals in Motion. Thank you."
