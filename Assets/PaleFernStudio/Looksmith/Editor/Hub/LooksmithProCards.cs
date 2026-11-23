using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace PaleFernStudio.Looksmith.Editor
{
    /// <summary>
    /// What Pro adds, shown in Lite only and only here, in the Hub's Pro tab: the Pro looks (each before and after) and
    /// outcome-led cards. Lite's own panels show only what Lite has. Nothing here opens on its own; the store page opens
    /// only from a button the user clicks (store rule §2.5.1.d), and the user can hide the tab.
    /// </summary>
    public static class LooksmithProCards
    {
        public sealed class Feature
        {
            public string title, outcome, includes, doc;
            public string image;   // a card picture (LooksmithProPreviews.Card), or null
        }

        public static readonly Feature[] Features =
        {
            new Feature
            {
                title = "Paint the whole frame", doc = LooksmithDocs.ArtStyles, image = "Art Styles",
                outcome = "Turn the finished image into a painting, a watercolour, an ink drawing, a comic page, crisp pixel art or an old TV, or knit the whole world.",
                includes = "Painterly, Watercolour, Ink & Hatching, Comic, Pixel Art, Retro & Lo-Fi (CRT and VHS) and Yarn, per-layer masks, and the Pixel Camera for shimmer-free pixel art.",
            },
            new Feature
            {
                title = "A day that moves", doc = LooksmithDocs.Sky, image = "Sky",
                outcome = "The sky, sun, fog and light move through the day on their own, and the air gets its depth.",
                includes = "The day-night cycle, all nine sky presets (Lite has three), height fog, ground mist, sun and moon shafts, cloud shadows and ambient occlusion.",
            },
            new Feature
            {
                title = "Water that belongs in your world", doc = LooksmithDocs.Water, image = "Water",
                outcome = "Lakes, rivers, oceans and lava lit by the same Look as everything else.",
                includes = "Looksmith Water (waves, foam, refraction, reflections, caustics), buoyancy, interaction ripples and nine presets.",
            },
            new Feature
            {
                title = "Meadows in the wind", doc = LooksmithDocs.GrassField, image = "Grass",
                outcome = "A GPU grass field over your terrain, swaying in the same wind as the rest of the world.",
                includes = "The Looksmith Grass Field with five styles.",
            },
            new Feature
            {
                title = "Lines that understand your objects", doc = LooksmithDocs.Outlines,
                outcome = "Clean outlines where one object meets another, and highlights for whatever the player can use.",
                includes = "Object and colour edges, lines per rendering layer, hull outlines, selection highlight and see-through silhouettes.",
            },
            new Feature
            {
                title = "A different look in every area", doc = LooksmithDocs.LookZones,
                outcome = "Caves, interiors and dream sequences blend into their own look as the player walks in.",
                includes = "Look Zones (box, sphere or any collider) with blend distance and priority.",
            },
            new Feature
            {
                title = "Characters and terrain", doc = LooksmithDocs.Materials,
                outcome = "Hair, skin and big landscapes that sit in the same stylized world.",
                includes = "Looksmith Character, Looksmith Terrain and Looksmith Lit FX shaders, and smooth normals for outlines.",
            },
        };

        /// <summary>Lite shows the Pro tab unless this user hid it (Project Settings > Looksmith shows it again).</summary>
        public static bool tabShown => !LooksmithEdition.isPro && !LooksmithUserSettings.instance.hideProTab;

        /// <summary>Hides or shows the Pro tab for this user (UserSettings: nothing in the project changes).</summary>
        public static void SetTabHidden(bool hidden)
        {
            var settings = LooksmithUserSettings.instance;
            settings.hideProTab = hidden;
            settings.Save();
            foreach (var hub in Resources.FindObjectsOfTypeAll<LooksmithHubWindow>())
                hub.Rebuild();
        }

        /// <summary>The Hub's Pro tab (Lite only).</summary>
        public static void Build(VisualElement page)
        {
            var h1 = new Label("Looksmith Pro");
            h1.AddToClassList("looksmith-h1");
            page.Add(h1);
            var intro = new Label("You're using Looksmith Lite. Its three looks and their variants, the lighting, the lines, Restyle, Posterize & Palette, the Finish and the sky are all yours to keep. Pro adds:");
            intro.AddToClassList("looksmith-muted");
            page.Add(intro);
            page.Add(ProLooks());
            foreach (var f in Features)
            {
                var card = new VisualElement { name = "pro-" + f.title };
                card.AddToClassList("looksmith-card");
                card.AddToClassList("looksmith-pro-card");
                var picture = f.image != null ? LooksmithProPreviews.Card(f.image) : null;
                if (picture != null)
                {
                    var image = new Image { image = picture, scaleMode = ScaleMode.ScaleAndCrop, name = "pro-card-image" };
                    image.AddToClassList("looksmith-pro-card-image");
                    card.Add(image);
                }
                var title = new Label(f.title);
                title.AddToClassList("looksmith-h2");
                card.Add(title);
                var outcome = new Label(f.outcome);
                outcome.AddToClassList("looksmith-text");
                card.Add(outcome);
                var includes = new Label(f.includes);
                includes.AddToClassList("looksmith-muted");
                card.Add(includes);
                var row = new VisualElement();
                row.AddToClassList("looksmith-row");
                var doc = f.doc;
                row.Add(new Button(() => LooksmithLinks.Open(doc)) { text = "Read about it", name = "pro-doc", tooltip = LooksmithLinks.Describe(doc) });
                row.Add(new Button(LooksmithEdition.OpenStore) { text = "See Looksmith Pro", name = "pro-store", tooltip = "Opens Pale Fern Studio's page on the Asset Store, where Looksmith Pro is, in your browser." });
                card.Add(row);
                page.Add(card);
            }
            var hide = new VisualElement();
            hide.AddToClassList("looksmith-row");
            hide.Add(new Button(() => SetTabHidden(true)) { text = "Hide this tab", name = "pro-hide", tooltip = "Hides the Pro tab for you. Project Settings > Looksmith shows it again." });
            hide.Add(new Label("Project Settings > Looksmith shows it again.") { style = { opacity = 0.7f } });
            page.Add(hide);
        }

        /// <summary>The Pro looks, each with its picture. A click shows it before and after on the demo village.</summary>
        static VisualElement ProLooks()
        {
            var section = new VisualElement { name = "pro-looks" };
            section.AddToClassList("looksmith-card");
            var pro = LooksmithLooks.All.Where(d => !d.lite).ToList();
            var head = new Label($"{pro.Count} more looks") { name = "pro-looks-title" };
            head.AddToClassList("looksmith-h2");
            section.Add(head);
            var note = new Label("From painted storybooks to VHS horror, each with its variants. Click one to see it before and after, on the demo village.");
            note.AddToClassList("looksmith-text");
            section.Add(note);
            var slot = new VisualElement { name = "pro-look-preview-slot" };
            section.Add(slot);
            var grid = new VisualElement { name = "pro-look-grid" };
            grid.AddToClassList("looksmith-gallery");
            foreach (var def in pro)
            {
                var d = def;
                var card = new Button(() => ShowProPreview(slot, d)) { name = "look-" + d.name, tooltip = d.description };
                card.AddToClassList("looksmith-look-card");
                var image = new Image { scaleMode = ScaleMode.ScaleAndCrop, name = "thumbnail" };
                image.AddToClassList("looksmith-look-thumb");
                var tex = LooksmithProPreviews.Thumbnail(d.name);
                if (tex != null)
                    image.image = tex;
                else
                    image.Add(LooksmithLookGallery.PaletteStrip(d, null));
                card.Add(image);
                var label = new Label(d.name);
                label.AddToClassList("looksmith-look-name");
                card.Add(label);
                grid.Add(card);
            }
            section.Add(grid);
            return section;
        }

        /// <summary>A Pro look before and after: its picture, with the plain one over the left of a line the slider moves.</summary>
        static void ShowProPreview(VisualElement slot, LooksmithLooks.Def d)
        {
            slot.Clear();
            var panel = new VisualElement { name = "pro-look-preview" };
            panel.AddToClassList("looksmith-card");
            panel.AddToClassList("looksmith-pro-preview");
            var title = new Label(d.name);
            title.AddToClassList("looksmith-h2");
            panel.Add(title);
            var text = new Label(d.description);
            text.AddToClassList("looksmith-text");
            panel.Add(text);

            var frame = new VisualElement { name = "preview-frame" };
            frame.AddToClassList("looksmith-preview-frame");
            var after = new Image { name = "preview-after", image = LooksmithProPreviews.Compare(d.name), scaleMode = ScaleMode.ScaleAndCrop };
            after.AddToClassList("looksmith-preview-image");
            var clip = new VisualElement { name = "preview-clip" };
            clip.AddToClassList("looksmith-preview-clip");
            var before = new Image { name = "preview-before", image = LooksmithProPreviews.Compare(LooksmithProPreviews.Plain), scaleMode = ScaleMode.ScaleAndCrop };
            before.AddToClassList("looksmith-preview-image");
            clip.Add(before);
            var line = new VisualElement { name = "preview-line" };
            line.AddToClassList("looksmith-preview-line");
            var left = new Label("Plain URP");
            left.AddToClassList("looksmith-preview-tag");
            left.AddToClassList("looksmith-preview-tag--left");
            var right = new Label(d.name);
            right.AddToClassList("looksmith-preview-tag");
            right.AddToClassList("looksmith-preview-tag--right");
            frame.Add(after);
            frame.Add(clip);
            frame.Add(line);
            frame.Add(left);
            frame.Add(right);
            var split = new Slider(0f, 1f) { value = 0.5f, name = "preview-split", tooltip = "Drag to move the line: plain URP on the left, the look on the right." };
            void Layout()
            {
                float w = frame.resolvedStyle.width;
                if (float.IsNaN(w) || w <= 0f)
                    return;
                float h = Mathf.Round(w * 0.625f);   // the pictures are 16:10
                frame.style.height = h;
                after.style.width = w;
                after.style.height = h;
                before.style.width = w;
                before.style.height = h;
                clip.style.width = w * split.value;
                line.style.left = w * split.value - 1f;
            }
            frame.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            split.RegisterValueChangedCallback(_ => Layout());
            panel.Add(frame);
            panel.Add(split);

            var row = new VisualElement();
            row.AddToClassList("looksmith-row");
            row.Add(new Button(LooksmithEdition.OpenStore) { text = "See Looksmith Pro", name = "pro-store", tooltip = "Opens Pale Fern Studio's page on the Asset Store, where Looksmith Pro is, in your browser." });
            row.Add(new Button(slot.Clear) { text = "Close", name = "pro-preview-close" });
            panel.Add(row);
            slot.Add(panel);
            panel.schedule.Execute(() => panel.GetFirstAncestorOfType<ScrollView>()?.ScrollTo(panel));   // in view, even scrolled down
        }
    }
}
