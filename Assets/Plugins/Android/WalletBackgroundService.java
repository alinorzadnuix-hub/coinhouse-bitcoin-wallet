package com.antigravity.wallet;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.util.Log;

import org.json.JSONObject;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

public class WalletBackgroundService extends Service {

    private static final String CHANNEL_ID = "wallet_background_channel";
    private static final int NOTIFICATION_ID = 1001;
    private static final String TAG = "WalletBackgroundService";

    private Handler handler;
    private Runnable runnable;
    private double lastPrice = 0;
    private String currency = "usd";
    private String currencySymbol = "$";
    private boolean isTestNet = false;

    @Override
    public void onCreate() {
        super.onCreate();
        createNotificationChannel();
        startForeground(NOTIFICATION_ID, createNotification("Starting Bitcoin monitor..."));

        handler = new Handler(Looper.getMainLooper());
        runnable = new Runnable() {
            @Override
            public void run() {
                fetchBitcoinPrice();
                handler.postDelayed(this, 60000); // Check every 60 seconds
            }
        };
        handler.post(runnable);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null) {
            String cur = intent.getStringExtra("currency");
            String sym = intent.getStringExtra("symbol");
            if (cur != null) currency = cur.toLowerCase();
            String testNetStr = intent.getStringExtra("isTestNet");
            isTestNet = "true".equals(testNetStr);
        }
        return START_STICKY;
    }

    private void fetchBitcoinPrice() {
        new Thread(new Runnable() {
            @Override
            public void run() {
                try {
                    URL url;
                    if (isTestNet) {
                        url = new URL("https://api.coinpaprika.com/v1/coins/tbtc-bitcoin-testnet/ohlcv/latest/");
                    } else {
                        url = new URL("https://blockchain.info/ticker");
                    }
                    
                    HttpURLConnection connection = (HttpURLConnection) url.openConnection();
                    connection.setRequestMethod("GET");
                    connection.setRequestProperty("User-Agent", "CoinHouseWallet/1.0");
                    connection.setConnectTimeout(5000);
                    connection.setReadTimeout(5000);

                    if (connection.getResponseCode() == 200) {
                        BufferedReader reader = new BufferedReader(new InputStreamReader(connection.getInputStream()));
                        StringBuilder response = new StringBuilder();
                        String line;
                        while ((line = reader.readLine()) != null) {
                            response.append(line);
                        }
                        reader.close();

                        double currentPrice = 0;
                        if (isTestNet) {
                            org.json.JSONArray jsonArray = new org.json.JSONArray(response.toString());
                            currentPrice = jsonArray.getJSONObject(0).getDouble("close");
                        } else {
                            JSONObject json = new JSONObject(response.toString());
                            String curKey = currency != null ? currency.toUpperCase() : "USD";
                            if (!json.has(curKey)) curKey = "USD";
                            currentPrice = json.getJSONObject(curKey).getDouble("last");
                        }
                        
                        String changeIndicator = "";
                        if (lastPrice > 0) {
                            if (currentPrice > lastPrice) changeIndicator = " 🟢 (+)";
                            else if (currentPrice < lastPrice) changeIndicator = " 🔴 (-)";
                            else changeIndicator = " ⚪ (=)";
                        }
                        lastPrice = currentPrice;

                        String formattedPrice;
                        if (isTestNet) {
                            formattedPrice = String.format("%,.4f", currentPrice); // Testnet price is very small, show 4 decimals
                        } else {
                            formattedPrice = String.format("%,.0f", currentPrice);
                        }
                        
                        String prefix = isTestNet ? "Live BTCT Price: " : "Live BTC Price: ";
                        String content = prefix + currencySymbol + formattedPrice + changeIndicator;
                        
                        updateNotification(content);
                    }
                } catch (Exception e) {
                    Log.e(TAG, "Error fetching BTC price", e);
                }
            }
        }).start();
    }

    private Notification createNotification(String text) {
        Notification.Builder builder;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            builder = new Notification.Builder(this, CHANNEL_ID);
        } else {
            builder = new Notification.Builder(this);
        }

        // Get the launcher intent to open the app when tapped
        Intent launchIntent = getPackageManager().getLaunchIntentForPackage(getPackageName());
        PendingIntent pendingIntent = null;
        if (launchIntent != null) {
            launchIntent.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
            int pendingFlags = PendingIntent.FLAG_UPDATE_CURRENT;
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) {
                pendingFlags |= PendingIntent.FLAG_IMMUTABLE;
            }
            pendingIntent = PendingIntent.getActivity(this, 0, launchIntent, pendingFlags);
        }

        // Use Unity's generated notification icon, fallback to default app icon
        int iconResId = getResources().getIdentifier("icon_0", "drawable", getPackageName());
        if (iconResId == 0) {
            iconResId = getResources().getIdentifier("app_icon", "mipmap", getPackageName());
        }
        if (iconResId == 0) {
            iconResId = android.R.drawable.ic_dialog_info; // Ultimate fallback
        }

        return builder
                .setContentTitle("Bitcoin Wallet")
                .setContentText(text)
                .setSmallIcon(iconResId)
                .setContentIntent(pendingIntent)
                .setOngoing(true)
                .setOnlyAlertOnce(true) // Don't vibrate/sound on every update
                .build();
    }

    private void updateNotification(String text) {
        NotificationManager manager = (NotificationManager) getSystemService(Context.NOTIFICATION_SERVICE);
        if (manager != null) {
            manager.notify(NOTIFICATION_ID, createNotification(text));
        }
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            // Use IMPORTANCE_LOW to prevent sound/vibration but allow showing on screen
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID,
                    "Live Price Monitor",
                    NotificationManager.IMPORTANCE_LOW
            );
            channel.setDescription("Keeps the wallet running in background to track BTC price");
            channel.setShowBadge(false);
            
            NotificationManager manager = getSystemService(NotificationManager.class);
            if (manager != null) {
                manager.createNotificationChannel(channel);
            }
        }
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null; // Not binding to anything
    }

    @Override
    public void onDestroy() {
        super.onDestroy();
        if (handler != null && runnable != null) {
            handler.removeCallbacks(runnable);
        }
    }
}
