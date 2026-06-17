namespace CopperMod.Sid
{
    /// <summary>
    /// Allows callers to select the SID emulation profile for loaded SID content.
    /// </summary>
    public interface ISidEmulationProfileController
    {
        /// <summary>
        /// Gets or sets the SID emulation profile.
        /// </summary>
        SidEmulationProfile SidEmulationProfile { get; set; }
    }
}
