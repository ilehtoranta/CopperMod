using Xunit;

// Emulator integration fixtures own large framebuffers, disk images, and machine
// graphs. Running several collections concurrently can exhaust the test host and
// abort the production matrix without producing a managed test failure.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
