using Goa2.Editor;

namespace Goa2.UI3D.Editor
{
    public static class Board3DAuditRunner
    {
        public static void Run()
        {
            BuildTools.Prepare();
            // Reuse the pinned-version Game View sizing utility, with its required
            // -goaCardReadingEditor flag. The actual audit is -goa3dAudit, not card audit.
            RevealedStripeAuditRunner.Run();
        }
    }
}
