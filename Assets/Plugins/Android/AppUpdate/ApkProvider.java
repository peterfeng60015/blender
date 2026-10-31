package com.peterfeng.blender;

// ASCII only in this file (see ApkInstaller.java).

import android.content.ContentProvider;
import android.content.ContentValues;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileNotFoundException;

/**
 * Serves the downloaded app APK to the system installer (Android 7+ forbids handing file:// URIs to other apps).
 * Read-only; only plain file names ending in .apk inside the cache folder (Application.temporaryCachePath).
 * A tiny provider of our own instead of androidx FileProvider, so the project needs no extra Gradle dependency.
 * The manifest entry is added at build time by Assets/Editor/AppUpdate/AppUpdateManifestPostProcessor.cs.
 */
public final class ApkProvider extends ContentProvider {
    public static final String AUTHORITY_SUFFIX = ".apkprovider";
    public static final String APK_MIME = "application/vnd.android.package-archive";

    @Override
    public boolean onCreate() {
        return true;
    }

    private File resolve(Uri uri) throws FileNotFoundException {
        String name = uri.getLastPathSegment();
        if (name == null || !name.endsWith(".apk") || name.contains("/") || name.contains(".."))
            throw new FileNotFoundException("not served: " + uri);
        File[] dirs = { getContext().getExternalCacheDir(), getContext().getCacheDir() };
        for (File dir : dirs) {
            if (dir == null)
                continue;
            File file = new File(dir, name);
            if (file.isFile())
                return file;
        }
        throw new FileNotFoundException("not found: " + name);
    }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (!"r".equals(mode))
            throw new FileNotFoundException("read-only");
        return ParcelFileDescriptor.open(resolve(uri), ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        File file;
        try {
            file = resolve(uri);
        } catch (FileNotFoundException e) {
            return null;
        }
        MatrixCursor cursor = new MatrixCursor(new String[] { OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE });
        cursor.addRow(new Object[] { file.getName(), file.length() });
        return cursor;
    }

    @Override
    public String getType(Uri uri) {
        return APK_MIME;
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) {
        throw new UnsupportedOperationException();
    }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) {
        throw new UnsupportedOperationException();
    }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {
        throw new UnsupportedOperationException();
    }
}
