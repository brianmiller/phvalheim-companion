using UnityEngine;

namespace PhValheimCompanion
{
    // Watches for the join actually LANDING, from somewhere that is still alive when it does.
    //
    // WHY THIS IS NOT JUST A LINE IN ConnectDialog.Update
    //
    // That was the first attempt and it could never have worked. ConnectDialog is
    // AddComponent'd onto FejdStartup's own GameObject (see FejdStartupPatch), and Valheim
    // destroys that object when it loads the game scene. So ConnectDialog.Update stops running
    // at the exact moment the player gets into the world -- the one window in which "did we get
    // in?" can be observed. The check was written, reviewed, built, shipped, and was unreachable
    // at the only time it mattered.
    //
    // ConnectFlow's state is static, so it survives the scene change; the component does not.
    // That asymmetry is the whole trap: the flags looked like they were being maintained across
    // the join because they persisted, while the code maintaining them had been destroyed.
    //
    // So this lives on its own GameObject with DontDestroyOnLoad: it is created once when the
    // plugin loads and runs in every scene, menu or world. Nothing else in the mod needs that
    // lifetime, which is why it is a separate object rather than a flag somewhere convenient.
    internal class JoinSentry : MonoBehaviour
    {
        private const string ObjectName = "PhValheimJoinSentry";

        internal static void Install()
        {
            // Idempotent: BepInEx loads a plugin once, but a second sentry would mean two
            // Updates racing on the same static and is cheap to rule out.
            if (GameObject.Find(ObjectName) != null) return;

            var go = new GameObject(ObjectName);
            Object.DontDestroyOnLoad(go);
            go.AddComponent<JoinSentry>();
            Main.StaticLogger.LogMessage("Join sentry installed; it reports when a join actually lands.");
        }

        private void Update()
        {
            // Only while an attempt is in flight. Outside one there is nothing to record, and
            // Player.m_localPlayer is non-null for every ordinary single-player session too --
            // reading it unconditionally would mark a join "landed" that never happened.
            if (!ConnectFlow.Connecting) return;

            // The local player object exists only once the player has spawned INTO the world.
            // Game and ZNetScene both come up earlier, while the main scene is still loading and
            // before the handshake has necessarily succeeded, so either of those would call a
            // join successful that was still about to fail.
            if (Player.m_localPlayer != null) ConnectFlow.NoticeJoined();
        }
    }
}
