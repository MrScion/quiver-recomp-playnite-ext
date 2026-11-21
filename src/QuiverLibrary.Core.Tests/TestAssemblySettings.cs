using Xunit;

// ExecutablePlanner.GamesDirOverride is static (set by the plugin from its settings): tests that change it
// must not run alongside tests that resolve {GamesDir}.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
