namespace QuiverLibrary.Core
{
    /// <summary>
    /// Plugin GUID as a plain string, with no PlayniteSDK dependency, so Core
    /// and any future sibling extension can refer to it without a live plugin
    /// instance.
    /// </summary>
    public static class PluginIdentity
    {
        public const string LibraryPluginGuid = "a48d03b5-791b-4b3c-ad89-1c8c88e81dd2";
    }
}
