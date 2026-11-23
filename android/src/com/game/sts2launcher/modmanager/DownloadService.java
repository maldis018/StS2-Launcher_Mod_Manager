package com.game.sts2launcher.modmanager;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.net.wifi.WifiManager;
import android.os.Build;
import android.os.IBinder;
import android.os.PowerManager;
import android.util.Log;

import java.util.Locale;

// Keeps the process alive while the launcher downloads game files.
//
// The download itself runs in C# on the Mono runtime inside this process; this
// service does no work of its own. Its only job is to tell Android that the
// app is doing something the user asked for, so the download keeps running
// with the screen off or the app in the background instead of being frozen or
// killed. It also holds a partial wake lock (CPU stays on) and a Wi-Fi lock.
//
// If the process dies anyway, nothing is lost: the C# downloader resumes at
// chunk level the next time the launcher opens. That is why this is
// START_NOT_STICKY: restarting the service without the launcher UI would have
// nothing to keep alive.
//
// Notification permission (Android 13+) is optional. If the user denies it the
// foreground service still runs; Android just doesn't show the notification in
// the shade (it stays visible in the system's active-apps list).
public class DownloadService extends Service {
	private static final String TAG = "STS2DownloadService";
	private static final String CHANNEL_ID = "game_download";
	private static final int NOTIFICATION_ID = 4201;
	private static final String EXTRA_TITLE = "title";
	private static final String EXTRA_TEXT = "text";
	// Upper bound in case stop() is never called (crash between start and stop).
	private static final long WAKE_LOCK_TIMEOUT_MS = 6L * 60 * 60 * 1000;

	private static volatile boolean running;
	private static volatile String currentTitle = "";

	private PowerManager.WakeLock wakeLock;
	private WifiManager.WifiLock wifiLock;

	static void start(Context ctx, String title, String text) {
		currentTitle = title != null ? title : "";
		Intent intent = new Intent(ctx, DownloadService.class)
				.putExtra(EXTRA_TITLE, currentTitle)
				.putExtra(EXTRA_TEXT, text != null ? text : "");
		try {
			if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
				ctx.startForegroundService(intent);
			} else {
				ctx.startService(intent);
			}
		} catch (Exception e) {
			// Android 12+ refuses to start a foreground service while the app
			// is in the background (ForegroundServiceStartNotAllowedException).
			// The download still runs; it is just less protected.
			Log.w(TAG, "Could not start download service", e);
		}
	}

	// Updates the notification in place. Does not (re)start the service, so it
	// is safe to call from the background and a no-op once stopped.
	static void update(Context ctx, int percent, String text) {
		if (!running) return;
		NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
		if (nm == null) return;
		try {
			nm.notify(NOTIFICATION_ID, buildNotification(ctx, currentTitle, text, percent));
		} catch (Exception e) {
			Log.w(TAG, "Could not update download notification", e);
		}
	}

	static void stop(Context ctx) {
		try {
			ctx.stopService(new Intent(ctx, DownloadService.class));
		} catch (Exception e) {
			Log.w(TAG, "Could not stop download service", e);
		}
	}

	@Override
	public int onStartCommand(Intent intent, int flags, int startId) {
		String title = intent != null ? intent.getStringExtra(EXTRA_TITLE) : null;
		String text = intent != null ? intent.getStringExtra(EXTRA_TEXT) : null;
		if (title != null) currentTitle = title;

		ensureChannel(this);
		Notification notification = buildNotification(this, currentTitle, text, -1);
		try {
			if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
				startForeground(NOTIFICATION_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_DATA_SYNC);
			} else {
				startForeground(NOTIFICATION_ID, notification);
			}
		} catch (Exception e) {
			Log.w(TAG, "startForeground failed; stopping service", e);
			stopSelf();
			return START_NOT_STICKY;
		}

		running = true;
		acquireLocks();
		return START_NOT_STICKY;
	}

	// Android 15+: dataSync foreground services get about 6 hours per day.
	// Stop cleanly when that runs out; the download continues unprotected and
	// resumes later if the process is frozen or killed.
	@Override
	public void onTimeout(int startId, int fgsType) {
		Log.w(TAG, "Foreground service time limit reached; stopping");
		stopSelf();
	}

	@Override
	public void onDestroy() {
		running = false;
		releaseLocks();
		super.onDestroy();
	}

	@Override
	public IBinder onBind(Intent intent) {
		return null;
	}

	@SuppressWarnings("deprecation")
	private void acquireLocks() {
		try {
			if (wakeLock == null) {
				PowerManager pm = (PowerManager) getSystemService(Context.POWER_SERVICE);
				wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "sts2launcher:download");
				wakeLock.setReferenceCounted(false);
			}
			wakeLock.acquire(WAKE_LOCK_TIMEOUT_MS);
		} catch (Exception e) {
			Log.w(TAG, "Wake lock unavailable", e);
		}
		try {
			if (wifiLock == null) {
				WifiManager wm = (WifiManager) getApplicationContext().getSystemService(Context.WIFI_SERVICE);
				// HIGH_PERF keeps Wi-Fi from dozing with the screen off on
				// Android 10 to 13. It is deprecated and has no effect on 14+,
				// where the foreground service itself is what keeps the
				// connection usable.
				wifiLock = wm.createWifiLock(WifiManager.WIFI_MODE_FULL_HIGH_PERF, "sts2launcher:download");
				wifiLock.setReferenceCounted(false);
			}
			wifiLock.acquire();
		} catch (Exception e) {
			Log.w(TAG, "Wi-Fi lock unavailable", e);
		}
	}

	private void releaseLocks() {
		try {
			if (wakeLock != null && wakeLock.isHeld()) wakeLock.release();
		} catch (Exception e) {
			Log.w(TAG, "Wake lock release failed", e);
		}
		try {
			if (wifiLock != null && wifiLock.isHeld()) wifiLock.release();
		} catch (Exception e) {
			Log.w(TAG, "Wi-Fi lock release failed", e);
		}
	}

	private static void ensureChannel(Context ctx) {
		if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return;
		NotificationManager nm = (NotificationManager) ctx.getSystemService(Context.NOTIFICATION_SERVICE);
		if (nm == null || nm.getNotificationChannel(CHANNEL_ID) != null) return;
		boolean ko = Locale.getDefault().getLanguage().startsWith("ko");
		NotificationChannel channel = new NotificationChannel(
				CHANNEL_ID,
				ko ? "게임 다운로드" : "Game downloads",
				NotificationManager.IMPORTANCE_LOW);
		channel.setShowBadge(false);
		nm.createNotificationChannel(channel);
	}

	// percent < 0 shows an indeterminate bar.
	@SuppressWarnings("deprecation")
	private static Notification buildNotification(Context ctx, String title, String text, int percent) {
		Notification.Builder b = Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
				? new Notification.Builder(ctx, CHANNEL_ID)
				: new Notification.Builder(ctx);
		Intent open = new Intent(ctx, GodotApp.class)
				.setFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_REORDER_TO_FRONT);
		PendingIntent pi = PendingIntent.getActivity(
				ctx, 0, open, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
		b.setSmallIcon(android.R.drawable.stat_sys_download)
				.setContentTitle(title)
				.setContentText(text != null ? text : "")
				.setContentIntent(pi)
				.setOngoing(true)
				.setOnlyAlertOnce(true)
				.setProgress(100, Math.max(0, Math.min(100, percent)), percent < 0);
		if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
			b.setForegroundServiceBehavior(Notification.FOREGROUND_SERVICE_IMMEDIATE);
		}
		return b.build();
	}
}
