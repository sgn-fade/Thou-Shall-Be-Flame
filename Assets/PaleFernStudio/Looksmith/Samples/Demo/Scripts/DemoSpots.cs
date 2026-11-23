using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace PaleFernStudio.Looksmith.Demo
{
    /// <summary>
    /// Places to find in the woods, each shown two ways: walk into one and its card offers two moods (1 and 2). A mood is
    /// a Look, a time of day and a few tweaks on top, and the card lists the tweaks: the same place, cozy or terrifying,
    /// and a look is a starting point, not a preset to take or leave.
    /// </summary>
    [AddComponentMenu("Looksmith/Demo/Demo Spots")]
    public sealed class DemoSpots : MonoBehaviour, IDemoPanel
    {
        [Serializable]
        public class Mood
        {
            public string title;
            [TextArea] public string line;
            [Tooltip("The Look, by its name on the showcase.")]
            public string look;
            public LooksmithSkyPreset sky;
            [Tooltip("Tweaks on top of the Look (what makes it this place's own).")]
            public LooksmithAdviceChange[] tweaks = Array.Empty<LooksmithAdviceChange>();
        }

        [Serializable]
        public class Spot
        {
            public string name;
            [TextArea] public string story;
            public Vector3 centre;
            [Min(1f)] public float radius = 10f;
            public Mood a = new Mood(), b = new Mood();
        }

        public Spot[] spots = Array.Empty<Spot>();

        DemoShowcase m_Showcase;
        VisualElement m_Card;
        Label m_Title, m_Story, m_Applied;
        VisualElement m_Moods, m_Tweaks;
        Spot m_Here;
        Mood m_Active;
        Spot m_ActiveSpot;

        /// <summary>The spot the player stands in (null in the open).</summary>
        public Spot here => m_Here;
        /// <summary>The mood on screen, until the Look is changed another way.</summary>
        public Mood activeMood => m_Active;

        void OnEnable()
        {
            m_Showcase = GetComponent<DemoShowcase>();
            if (m_Showcase != null)
                m_Showcase.LookChanged += OnLookChanged;
        }

        void OnDisable()
        {
            if (m_Showcase != null)
                m_Showcase.LookChanged -= OnLookChanged;
        }

        bool m_Applying;

        void OnLookChanged()
        {
            if (!m_Applying)
                m_Active = null;
        }

        /// <summary>The spot containing a point (the nearest when they overlap).</summary>
        public Spot SpotAt(Vector3 p)
        {
            Spot best = null;
            float bestD = float.MaxValue;
            foreach (var s in spots)
            {
                var d = new Vector2(p.x - s.centre.x, p.z - s.centre.z).magnitude;
                if (d < s.radius && d < bestD)
                {
                    best = s;
                    bestD = d;
                }
            }
            return best;
        }

        /// <summary>Shows a mood: its Look (a fresh copy), its tweaks on the copy, its time of day.</summary>
        public void Apply(Spot spot, Mood mood)
        {
            // only on the showcase's Play Mode copy: outside Play Mode the profile on screen is the Look asset itself
            if (m_Showcase == null || mood == null || !Application.isPlaying)
                return;
            m_Applying = true;
            try
            {
                m_Showcase.ShowLook(mood.look, false);
                var profile = m_Showcase.workingProfile;
                foreach (var t in mood.tweaks)
                    LooksmithLookAdvisor.Set(profile, t);
            }
            finally
            {
                m_Applying = false;
            }
            m_Active = mood;
            m_ActiveSpot = spot;
            m_Showcase.ShowSky(mood.sky, mood.title, $"{mood.look}  ·  {SkyName(mood.sky)}");
            Refresh();
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;
            var player = DemoPlayer.current;
            var here = player != null ? SpotAt(player.transform.position) : null;
            if (here != m_Here)
            {
                m_Here = here;
                Refresh();
            }
            if (m_Here == null)
                return;
            if (DemoInput.Pressed(DemoKey.MoodA))
                Apply(m_Here, m_Here.a);
            if (DemoInput.Pressed(DemoKey.MoodB))
                Apply(m_Here, m_Here.b);
        }

        /// <summary>A sky preset's name without its " Sky" suffix.</summary>
        public static string SkyName(LooksmithSkyPreset p) => p == null ? "" : p.name.EndsWith(" Sky") ? p.name.Substring(0, p.name.Length - 4) : p.name;

        // ------------------------------------------------------------ UI

        public void Build(VisualElement root)
        {
            m_Card = DemoShowcase.Card();
            m_Card.style.right = 28;
            m_Card.style.bottom = 96;
            m_Card.style.width = 420;
            var tag = DemoShowcase.Label("A PLACE TO FIND", 11, DemoShowcase.AccentColour, FontStyle.Bold);
            tag.style.letterSpacing = 2;
            m_Card.Add(tag);
            m_Title = DemoShowcase.Label("", 24, DemoShowcase.TextColour, FontStyle.Bold);
            m_Card.Add(m_Title);
            m_Story = DemoShowcase.Label("", 13, DemoShowcase.MutedColour);
            m_Story.style.marginBottom = 8;
            m_Card.Add(m_Story);
            m_Moods = new VisualElement();
            m_Card.Add(m_Moods);
            m_Applied = DemoShowcase.Label("", 12, DemoShowcase.AccentColour, FontStyle.Bold);
            m_Applied.style.marginTop = 8;
            m_Card.Add(m_Applied);
            m_Tweaks = new VisualElement();
            m_Card.Add(m_Tweaks);
            root.Add(m_Card);
            Refresh();
        }

        public void Refresh()
        {
            if (m_Card == null)
                return;
            m_Card.style.display = m_Here != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (m_Here == null || m_Showcase == null)
                return;
            m_Title.text = m_Here.name;
            m_Story.text = m_Here.story;
            m_Moods.Clear();
            foreach (var (key, mood) in new[] { ("1", m_Here.a), ("2", m_Here.b) })
            {
                bool on = mood == m_Active;
                var row = m_Showcase.Row(key, (on ? "▶ " : "") + mood.title, $"{mood.look} · {SkyName(mood.sky)}");
                m_Moods.Add(row);
                var line = DemoShowcase.Label(mood.line, 12, DemoShowcase.MutedColour);
                line.style.marginLeft = 34;
                line.style.marginBottom = 6;
                m_Moods.Add(line);
            }
            m_Tweaks.Clear();
            if (m_Active != null && m_ActiveSpot == m_Here)
            {
                m_Applied.text = m_Active.tweaks.Length > 0 ? $"{m_Active.look}, CUSTOMISED FOR THIS PLACE" : $"{m_Active.look}, AS IT SHIPS";
                foreach (var t in m_Active.tweaks)
                    m_Tweaks.Add(DemoShowcase.Label("• " + t.label, 12, DemoShowcase.TextColour));
                if (LooksmithTweaksHooks.overlayAvailable)
                    m_Tweaks.Add(DemoShowcase.Label("Press Y to change any of this yourself in Tweaks. Save keeps it.", 11, DemoShowcase.MutedColour));
            }
            else
                m_Applied.text = "";
        }
    }
}
