# قانون حفظ رابط کاربری و معماری پایدار (UI Preservation & Architecture Rule)

## ۱. اصل عدم تغییر رابط کاربری (UI Immutability Principle)
- رابط کاربری فعلی پروژه CoinHouse در بالاترین سطح کیفی، چیدمان و پاسخ‌گویی قرار دارد.
- هیچ کلاسی در `Assets/Scripts 1/` که وظیفه رندر و نمایش UI را دارد (نظیر `ModernMobileWallet`, `FinalWalletUI`, `SendReceiveUI`, `TransactionHistoryUI`, `RecoverWalletUI`, `MiningUI`, `AppLockUI`) نباید مورد بازطراحی، جابجایی عناصر، تغییر رنگ، سایز المان‌ها یا فونت قرار گیرد.

## ۲. الگوی مقیاس‌پذیری و تشخیص پلتفرم (Platform Detection Pattern)
تمام اسکریپت‌ها موظف به رعایت الگوی زیر جهت سازگاری کامل همزمان در Desktop و شبیه‌سازهای Mobile هستند:
```csharp
bool isDesktop = false;
#if UNITY_STANDALONE && !UNITY_EDITOR
isDesktop = true;
#elif UNITY_EDITOR
isDesktop = (Screen.width > Screen.height);
#endif
```
هیچ‌گاه شرط `#if UNITY_STANDALONE || UNITY_EDITOR` به تنهایی بازگردانده نشود زیرا حالت شبیه‌ساز موبایل را دچار اختلال می‌کند.

## ۳. عدم دستکاری در بیلد و آیکون‌ها
- سیستم سوئیچینگ بین MainNet و TestNet متکی بر `AndroidBuilder.cs` و `BuildVariantSwitcher.cs` است.
- مسیر آیکون‌ها ثابت و مشخص است:
  - `Assets/Resources/y.png` (شبکه اصلی)
  - `Assets/Resources/Bitcointa.png` (شبکه تستی)
- استفاده از ساختار `NotifIcon.androidlib` برای نگهداری آیکون اعلان‌های نیتیو اندروید اجباری است و پوشه منسوخ `Assets/Plugins/Android/res` نباید ایجاد شود.
