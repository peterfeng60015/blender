package com.peterfeng.blender;

// ASCII only in this file: the Gradle project Unity generates does not force UTF-8, and javac on a
// Chinese Windows reads sources as cp950, so non-ASCII text (even in comments) can break the build.

import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

import java.io.File;

/**
 * App (APK) self-update, called from Assets/Entry/AppUpdateChecker.cs via AndroidJavaClass:
 * installed versionCode, the "install unknown apps" permission, and launching the system installer.
 * Android never lets a normal app install silently; the last step is always the system confirm screen.
 */
public final class ApkInstaller {
    private ApkInstaller() {
    }

    /** versionCode of the installed app. */
    public static long versionCode(Activity activity) throws Exception {
        PackageInfo info = activity.getPackageManager().getPackageInfo(activity.getPackageName(), 0);
        if (Build.VERSION.SDK_INT >= 28)
            return info.getLongVersionCode();
        return info.versionCode;
    }

    /** Android 8+ needs a per-app "install unknown apps" grant; older versions use a global setting (the installer enforces it). */
    public static boolean canInstall(Activity activity) {
        if (Build.VERSION.SDK_INT < 26)
            return true;
        return activity.getPackageManager().canRequestPackageInstalls();
    }

    /** Opens this app's "install unknown apps" settings page; the player grants it and comes back. */
    public static void openInstallSettings(Activity activity) {
        Intent intent = new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,
                Uri.parse("package:" + activity.getPackageName()));
        activity.startActivity(intent);
    }

    /** Launches the system installer. apkPath must be in a folder ApkProvider serves (Application.temporaryCachePath). */
    public static void install(Activity activity, String apkPath) {
        File file = new File(apkPath);
        Uri uri = Uri.parse("content://" + activity.getPackageName() + ApkProvider.AUTHORITY_SUFFIX + "/" + Uri.encode(file.getName()));
        Intent intent = new Intent(Intent.ACTION_VIEW);
        intent.setDataAndType(uri, ApkProvider.APK_MIME);
        intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION | Intent.FLAG_ACTIVITY_NEW_TASK);
        activity.startActivity(intent);
    }
}
