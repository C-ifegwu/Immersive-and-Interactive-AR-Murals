namespace ARMurals
{
    /// <summary>
    /// The only contract between the shared AR rig and an individual mural experience.
    /// Every mural owner implements this (via MuralExperienceBase) inside their own folder.
    /// Nothing else crosses the boundary between mural slices.
    /// </summary>
    public interface IMuralExperience
    {
        /// <summary>Slot id: "M1".."M5". Must match the reference image name in the shared library.</summary>
        string MuralSlot { get; }

        /// <summary>The mural has been detected and is being tracked. Begin or resume.</summary>
        void OnTrackingFound();

        /// <summary>Tracking has been lost. Hide digital content but keep state.</summary>
        void OnTrackingLost();

        /// <summary>Return the mural to its untouched state.</summary>
        void OnReset();
    }
}
