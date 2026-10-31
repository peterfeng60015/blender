using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

/// <summary>
/// Android 出包時在 unityLibrary 的 AndroidManifest.xml 加上主體更新要的東西（見 Assets/Plugins/Android/AppUpdate）：
///   - REQUEST_INSTALL_PACKAGES：Android 8 起叫出系統安裝畫面要這個權限（玩家還要在設定裡允許這個 App 安裝未知應用程式）。
///   - ApkProvider：把下載好的 APK 以 content:// 交給系統安裝程式。authorities 用 ${applicationId}，manifest 合併時換成套件名。
/// 用 post-generate 改產生出來的 manifest，不放自訂的主 manifest：不必跟著 Unity 改版維護一整份 manifest。可重複執行。
/// </summary>
public class AppUpdateManifestPostProcessor : IPostGenerateGradleAndroidProject
{
    private const string AndroidNs = "http://schemas.android.com/apk/res/android";
    private const string InstallPermission = "android.permission.REQUEST_INSTALL_PACKAGES";
    private const string ProviderClass = "com.peterfeng.blender.ApkProvider";

    public int callbackOrder => 0;

    public void OnPostGenerateGradleAndroidProject(string path)
    {
        var manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
        var doc = new XmlDocument();
        doc.Load(manifestPath);
        var manifest = doc.DocumentElement;
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("android", AndroidNs);

        if (manifest.SelectSingleNode($"uses-permission[@android:name='{InstallPermission}']", ns) == null)
        {
            var permission = doc.CreateElement("uses-permission");
            permission.SetAttribute("name", AndroidNs, InstallPermission);
            manifest.PrependChild(permission);
        }

        var application = manifest.SelectSingleNode("application") as XmlElement;
        if (application == null)
        {
            application = doc.CreateElement("application");
            manifest.AppendChild(application);
        }
        if (application.SelectSingleNode($"provider[@android:name='{ProviderClass}']", ns) == null)
        {
            var provider = doc.CreateElement("provider");
            provider.SetAttribute("name", AndroidNs, ProviderClass);
            provider.SetAttribute("authorities", AndroidNs, "${applicationId}.apkprovider");
            provider.SetAttribute("exported", AndroidNs, "false");
            provider.SetAttribute("grantUriPermissions", AndroidNs, "true");
            application.AppendChild(provider);
        }

        doc.Save(manifestPath);
        Debug.Log("[AppUpdateManifestPostProcessor] 已加上 REQUEST_INSTALL_PACKAGES 與 ApkProvider：" + manifestPath);
    }
}
