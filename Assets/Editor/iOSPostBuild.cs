
#if UNITY_IOS
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using System.Diagnostics;
using System.IO;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class iOSPostBuild
{
    // Runs first — plist edits
    [PostProcessBuild(10)]
    public static void OnPostProcessBuild(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        string plistPath = Path.Combine(path, "Info.plist");
        if (File.Exists(plistPath))
        {
            var plist = new PlistDocument();
            plist.ReadFromFile(plistPath);
            var root = plist.root;

            // Location
            if (!root.values.ContainsKey("NSLocationWhenInUseUsageDescription"))
                root.SetString("NSLocationWhenInUseUsageDescription", "InkJourney uses your location to show stories near you and place yours on the map.");

            // Camera
            if (!root.values.ContainsKey("NSCameraUsageDescription"))
                root.SetString("NSCameraUsageDescription", "InkJourney uses your camera so you can add photos to your stories.");

            // App Tracking Transparency
            if (!root.values.ContainsKey("NSUserTrackingUsageDescription"))
                root.SetString("NSUserTrackingUsageDescription", "This helps us understand how people use InkJourney so we can improve the experience.");

            // Background modes — audio + remote notifications
            if (!root.values.ContainsKey("UIBackgroundModes"))
            {
                var bgModes = root.CreateArray("UIBackgroundModes");
                bgModes.AddString("audio");
                bgModes.AddString("remote-notification");
            }
            else
            {
                var bgModes = root["UIBackgroundModes"].AsArray();
                bool hasAudio = false, hasRemote = false;
                if (bgModes != null)
                    foreach (var v in bgModes.values)
                    {
                        if (v.AsString() == "audio")               hasAudio  = true;
                        if (v.AsString() == "remote-notification") hasRemote = true;
                    }
                if (!hasAudio)  bgModes?.AddString("audio");
                if (!hasRemote) bgModes?.AddString("remote-notification");
            }

            plist.WriteToFile(plistPath);
            Debug.Log("[PostBuild] Info.plist updated.");
        }
        else
        {
            Debug.LogError("[PostBuild] Info.plist not found.");
        }
    }

    // Disable user script sandboxing — prevents PhaseScriptExecution build failure
    [PostProcessBuild(11)]
    public static void DisableScriptSandboxing(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        string projPath = PBXProject.GetPBXProjectPath(path);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);

        // Set on project level (covers all targets including GameAssembly)
        proj.SetBuildProperty(proj.ProjectGuid(), "ENABLE_USER_SCRIPT_SANDBOXING", "NO");

        // Also set on each known target explicitly
        var targets = new[]
        {
            proj.GetUnityMainTargetGuid(),
            proj.GetUnityFrameworkTargetGuid()
        };

        foreach (string guid in targets)
        {
            if (string.IsNullOrEmpty(guid)) continue;
            proj.SetBuildProperty(guid, "ENABLE_USER_SCRIPT_SANDBOXING", "NO");
        }

        proj.WriteToFile(projPath);

        // Brute-force: replace any remaining YES in the raw file (covers GameAssembly and any other targets)
        string raw = File.ReadAllText(projPath);
        string patched = raw.Replace("ENABLE_USER_SCRIPT_SANDBOXING = YES", "ENABLE_USER_SCRIPT_SANDBOXING = NO");
        if (patched != raw) File.WriteAllText(projPath, patched);

        Debug.Log("[PostBuild] ENABLE_USER_SCRIPT_SANDBOXING set to NO on all targets.");
    }

    // Add Push Notifications entitlement — required for APNs registration and FCM token generation
    [PostProcessBuild(12)]
    public static void AddPushNotificationsCapability(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        string projPath = PBXProject.GetPBXProjectPath(path);
        var capManager = new ProjectCapabilityManager(projPath, "Unity-iPhone/Unity-iPhone.entitlements", "Unity-iPhone");
        capManager.AddPushNotifications(false); // false = production APNs
        capManager.WriteToFile();
        Debug.Log("[PostBuild] Push Notifications capability added.");
    }

    // Add AppTrackingTransparency.framework + inject PrivacyInfo.xcprivacy
    [PostProcessBuild(13)]
    public static void AddPrivacyRequirements(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        string projPath = PBXProject.GetPBXProjectPath(path);
        var proj = new PBXProject();
        proj.ReadFromFile(projPath);

        string mainTarget = proj.GetUnityMainTargetGuid();

        // AppTrackingTransparency.framework — required for ATT prompt
        proj.AddFrameworkToProject(mainTarget, "AppTrackingTransparency.framework", false);
        proj.AddFrameworkToProject(proj.GetUnityFrameworkTargetGuid(), "AppTrackingTransparency.framework", false);

        // UserNotifications.framework — required for notification permission checks
        proj.AddFrameworkToProject(mainTarget, "UserNotifications.framework", false);
        proj.AddFrameworkToProject(proj.GetUnityFrameworkTargetGuid(), "UserNotifications.framework", false);

        // PrivacyInfo.xcprivacy — required since iOS 17 / May 2024
        string manifestDst = Path.Combine(path, "PrivacyInfo.xcprivacy");
        File.WriteAllText(manifestDst, PrivacyManifestContent());
        string fileGuid = proj.AddFile(manifestDst, "PrivacyInfo.xcprivacy", PBXSourceTree.Source);
        proj.AddFileToBuild(mainTarget, fileGuid);

        proj.WriteToFile(projPath);
        Debug.Log("[PostBuild] ATT framework and PrivacyInfo.xcprivacy added.");
    }

    private static string PrivacyManifestContent() => @"<?xml version=""1.0"" encoding=""UTF-8""?>
<!DOCTYPE plist PUBLIC ""-//Apple//DTD PLIST 1.0//EN"" ""http://www.apple.com/DTDs/PropertyList-1.0.dtd"">
<plist version=""1.0"">
<dict>
    <key>NSPrivacyTracking</key>
    <false/>
    <key>NSPrivacyTrackingDomains</key>
    <array/>
    <key>NSPrivacyCollectedDataTypes</key>
    <array>
        <dict>
            <key>NSPrivacyCollectedDataType</key>
            <string>NSPrivacyCollectedDataTypePreciseLocation</string>
            <key>NSPrivacyCollectedDataTypeLinked</key>
            <true/>
            <key>NSPrivacyCollectedDataTypeTracking</key>
            <false/>
            <key>NSPrivacyCollectedDataTypePurposes</key>
            <array>
                <string>NSPrivacyCollectedDataTypePurposeAppFunctionality</string>
                <string>NSPrivacyCollectedDataTypePurposeAnalytics</string>
            </array>
        </dict>
        <dict>
            <key>NSPrivacyCollectedDataType</key>
            <string>NSPrivacyCollectedDataTypeUserID</string>
            <key>NSPrivacyCollectedDataTypeLinked</key>
            <true/>
            <key>NSPrivacyCollectedDataTypeTracking</key>
            <false/>
            <key>NSPrivacyCollectedDataTypePurposes</key>
            <array>
                <string>NSPrivacyCollectedDataTypePurposeAppFunctionality</string>
            </array>
        </dict>
        <dict>
            <key>NSPrivacyCollectedDataType</key>
            <string>NSPrivacyCollectedDataTypeProductInteraction</string>
            <key>NSPrivacyCollectedDataTypeLinked</key>
            <false/>
            <key>NSPrivacyCollectedDataTypeTracking</key>
            <false/>
            <key>NSPrivacyCollectedDataTypePurposes</key>
            <array>
                <string>NSPrivacyCollectedDataTypePurposeAnalytics</string>
            </array>
        </dict>
        <dict>
            <key>NSPrivacyCollectedDataType</key>
            <string>NSPrivacyCollectedDataTypePhotosOrVideos</string>
            <key>NSPrivacyCollectedDataTypeLinked</key>
            <true/>
            <key>NSPrivacyCollectedDataTypeTracking</key>
            <false/>
            <key>NSPrivacyCollectedDataTypePurposes</key>
            <array>
                <string>NSPrivacyCollectedDataTypePurposeAppFunctionality</string>
            </array>
        </dict>
    </array>
    <key>NSPrivacyAccessedAPITypes</key>
    <array>
        <dict>
            <key>NSPrivacyAccessedAPIType</key>
            <string>NSPrivacyAccessedAPICategoryUserDefaults</string>
            <key>NSPrivacyAccessedAPITypeReasons</key>
            <array>
                <string>CA92.1</string>
            </array>
        </dict>
        <dict>
            <key>NSPrivacyAccessedAPIType</key>
            <string>NSPrivacyAccessedAPICategoryFileTimestamp</string>
            <key>NSPrivacyAccessedAPITypeReasons</key>
            <array>
                <string>C617.1</string>
            </array>
        </dict>
        <dict>
            <key>NSPrivacyAccessedAPIType</key>
            <string>NSPrivacyAccessedAPICategorySystemBootTime</string>
            <key>NSPrivacyAccessedAPITypeReasons</key>
            <array>
                <string>35F9.1</string>
            </array>
        </dict>
        <dict>
            <key>NSPrivacyAccessedAPIType</key>
            <string>NSPrivacyAccessedAPICategoryDiskSpace</string>
            <key>NSPrivacyAccessedAPITypeReasons</key>
            <array>
                <string>E174.1</string>
            </array>
        </dict>
    </array>
</dict>
</plist>";

    // Runs second — pod install so Podfile.lock is always in sync with the build output
    [PostProcessBuild(20)]
    public static void RunPodInstall(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        string podfilePath = Path.Combine(path, "Podfile");
        if (!File.Exists(podfilePath))
        {
            Debug.LogWarning("[PostBuild] Podfile not found — skipping pod install.");
            return;
        }

        Debug.Log("[PostBuild] Running pod install…");

        var psi = new ProcessStartInfo
        {
            FileName               = "/bin/bash",
            Arguments              = $"-c \"cd '{path}' && pod install\"",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using (var proc = Process.Start(psi))
        {
            proc.WaitForExit(180_000); // 3-minute timeout
            string output = proc.StandardOutput.ReadToEnd();
            string err    = proc.StandardError.ReadToEnd();
            if (proc.ExitCode == 0)
                Debug.Log($"[PostBuild] pod install succeeded.\n{output}");
            else
                Debug.LogError($"[PostBuild] pod install failed (exit {proc.ExitCode}):\n{err}");
        }

        // Ensure CocoaPods xcconfig stub files exist — pod install omits them when there are no pods,
        // but the Xcode project still references them and will fail to build without them.
        string[] podTargets = { "Pods-Unity-iPhone", "Pods-UnityFramework" };
        string[] configs    = { "debug", "release", "releaseforrunning", "releaseforprofiling" };
        foreach (string podTarget in podTargets)
        {
            string dir = Path.Combine(path, "Pods", "Target Support Files", podTarget);
            Directory.CreateDirectory(dir);
            foreach (string cfg in configs)
            {
                string xcconfig = Path.Combine(dir, $"{podTarget}.{cfg}.xcconfig");
                if (!File.Exists(xcconfig)) File.WriteAllText(xcconfig, "");
            }
        }
        Debug.Log("[PostBuild] CocoaPods xcconfig stubs ensured.");
    }

    // Runs third — resolves Firebase Swift Package Manager dependencies
    // so Xcode never shows "Missing package product" errors after a Unity build.
    [PostProcessBuild(50)]
    public static void ResolveFirebasePackages(BuildTarget target, string path)
    {
        if (target != BuildTarget.iOS) return;

        string workspace = Path.Combine(path, "Unity-iPhone.xcworkspace");
        if (!Directory.Exists(workspace))
        {
            Debug.LogWarning("[PostBuild] xcworkspace not found — skipping SPM resolve.");
            return;
        }

        Debug.Log("[PostBuild] Resolving Swift Package Manager dependencies (Firebase)…");

        var psi = new ProcessStartInfo
        {
            FileName               = "/usr/bin/xcodebuild",
            Arguments              = $"-resolvePackageDependencies -workspace \"{workspace}\" -scheme Unity-iPhone",
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        using (var proc = Process.Start(psi))
        {
            proc.WaitForExit(120_000); // 2-minute timeout
            string err = proc.StandardError.ReadToEnd();
            if (proc.ExitCode == 0)
                Debug.Log("[PostBuild] Firebase packages resolved successfully.");
            else
                Debug.LogWarning($"[PostBuild] SPM resolve exited {proc.ExitCode}: {err}");
        }
    }
}
#endif