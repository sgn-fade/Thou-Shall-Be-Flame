Looksmith Lite
by Pale Fern Studio
=========================================================================

Stylized rendering for URP, free. One Look drives the lighting, colour,
lines, haze and finish of everything it styles: Stylized Realism, Cel
Anime and Silent Fog, with their variants, and two Art Styles that stack
on them (Posterize & Palette, and the Finish).

Looksmith Pro adds 15 more Looks, seven more Art Styles and the world
tools (the day-night cycle, atmosphere, water, grass, Look Zones). The
Hub's Pro tab shows them. Lite is Pro with those files left out, so
importing Pro over Lite upgrades it in place, and your Looks and
restyled materials carry over.


-------------------------------------------------------------------------
QUICK START
-------------------------------------------------------------------------

1. Use URP, editor version 6000.3 or newer.
2. Open Window > Looksmith > Hub (it opens by itself after import).
3. On the Home tab: Scan my project, then Next: preview changes, then
   Apply. Pick a Look in the gallery.

Every change is previewed first and backed up, and can be reverted in
Hub > Changes.

The demo: open Samples/Demo/LooksmithDemoLite.unity and press Play. The
keys are listed on screen and in the manual.


-------------------------------------------------------------------------
DOCUMENTATION
-------------------------------------------------------------------------

Documentation/Looksmith Manual.pdf is the full manual, with a table of
contents: setup, every Look and Art Style, Restyle, backups and revert,
a scripting reference, known limitations and troubleshooting. It covers
both editions and marks what only Pro has.

Every field in Looksmith's inspectors has a tooltip, and the ? in each
header opens this manual (its contents and bookmarks lead to each
section).


-------------------------------------------------------------------------
WHAT IMPORTING CHANGES
-------------------------------------------------------------------------

Nothing outside this folder. Importing doesn't touch your project
settings, your URP asset or your scripting defines, and your renderer
needs no edits. The only thing that runs by itself is the Hub window,
which opens once per user and project.

Looksmith writes files only when you ask it to, after a preview:
  - Assets/Looksmith Generated/   your copies of Looks and materials
  - LooksmithBackups/             backups, next to Assets (never imported)


-------------------------------------------------------------------------
REMOVING LOOKSMITH
-------------------------------------------------------------------------

Hub > Changes > Uninstall: revert all, remove the scene roots, then delete
this folder. Looksmith never deletes its own package.


-------------------------------------------------------------------------
THIRD-PARTY CONTENT
-------------------------------------------------------------------------

None. Every script, shader, model, texture and preset in this package was
created for it by Pale Fern Studio.


-------------------------------------------------------------------------
SUPPORT
-------------------------------------------------------------------------

paleferndev@gmail.com

Please include your editor and URP versions, the rendering path
(Forward+, Forward or Deferred) and a screenshot if you can.
