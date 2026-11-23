namespace PaleFernStudio.Looksmith
{
    /// <summary>
    /// The manual's sections. The manual ships in the package (Documentation/Looksmith Manual.pdf) and every help button
    /// opens it; nothing is online. Each name is the section's title in the PDF (its bookmarks and contents), so a button's
    /// tooltip can say where to look. A "#part" after it names a part of that section.
    /// </summary>
    public static class LooksmithDocs
    {
        public const string Home = "Contents";
        public const string GettingStarted = "Getting started";
        public const string Hub = "The Hub";
        public const string Overlay = "The Scene view overlay";
        public const string Look = "Looks";
        public const string Palette = "Looks#colour";
        public const string Lines = "Lines";
        public const string ArtStyles = "Art Styles";
        public const string Materials = "Materials";
        public const string Restyle = "Restyle";
        public const string Backups = "Changes, backups and revert";
        public const string Water = "Water";
        public const string Sky = "Sky and time of day";
        public const string Atmosphere = "Atmosphere";
        public const string Wind = "Wind";
        public const string GrassField = "Grass Field";
        public const string LookZones = "Look Zones";
        public const string Outlines = "Outlines, highlight and see-through";
        public const string PixelCamera = "The Pixel Camera";
        public const string Cameras = "Cameras and rendering";
        public const string ProjectSettings = "Project Settings";
        public const string KnownLimitations = "Known limitations";
        public const string Troubleshooting = "Troubleshooting and FAQ";

        /// <summary>Where a reference points in the manual, for a tooltip: "Looks, colour".</summary>
        public static string Describe(string section) =>
            string.IsNullOrEmpty(section) ? "the manual" : "the manual: " + section.Replace("#", ", ");
    }
}
