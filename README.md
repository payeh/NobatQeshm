# سامانه نوبت قشم — نسخه C# / WPF

نسخه‌ی بومی ویندوز برنامه‌ی نوبت‌دهی و پایش خدمات تعویض پلاک قشم، بازنویسی‌شده با C#، WPF و .NET 8. این پروژه همه‌ی قابلیت‌های نسخه‌ی Python را بدون نیاز به نصب Python اجرا می‌کند و رابط آن بر اساس ظاهر برنامه‌ی `Citizen-Reg` طراحی شده است.

## امکانات

- ورود با کد ملی، موبایل، کپچا و کد فعال‌سازی (OTP)
- تشخیص خودکار کد امنیتی SVG و امکان ورود دستی
- نگهداری چند حساب و بازیابی نشست بدون دریافت دوباره OTP
- رمزگذاری کوکی‌ها با Windows DPAPI برای کاربر جاری ویندوز
- نمایش کامل مشخصات فردی، اقامت، شغل، اطلاعات تکمیلی و عکس کاربر
- مدیریت پلاک‌های محلی و اعتبارسنجی آن‌ها با سامانه
- مشاهده، افزودن، ویرایش و حذف پلاک‌های سامانه
- فشرده‌سازی تصاویر کارت خودرو مطابق رفتار سایت: JPEG با کیفیت ۷۰٪، حداکثر `8000×8000` و Data URL Base64
- مشاهده نرخ خدمات پل و لافت
- مشاهده سوابق تردد، صفحه‌بندی و جزئیات امن‌شده
- دریافت زنده نوع خدمت‌ها از صفحه‌ی نوبت‌دهی
- جست‌وجوی نوبت آزاد و ثبت نهایی با قرارداد واقعی API
- نمایش نوبت‌های قبلی و کپی کد رزرو
- ساخت، چاپ، ذخیره و کپی تصویری رسید
- ذخیره خودکار رسید موفق روی Desktop
- متن تبلیغاتی قابل تنظیم در رسید
- پایش چند پلاک/خدمت با فاصله زمانی مستقل
- هشدار صوتی چندباره و اعلان روی صفحه در صورت پیدا شدن نوبت
- صفحه‌ی تشخیص قرارداد API و وضعیت نشست/CSRF
- گزارش زنده عملیات و نمایش اطلاعات سیستم

## سازگاری با نسخه Python

برنامه از همان مسیر داده‌ی نسخه‌ی Python استفاده می‌کند:

```text
%LOCALAPPDATA%\QeshmNobatGUI
```

بنابراین حساب‌های ذخیره‌شده، کوکی‌های DPAPI، پلاک‌ها و تنظیم تبلیغات قبلی قابل استفاده‌اند. فایل‌های حساس فقط برای همان حساب ویندوز قابل رمزگشایی هستند.

## پیش‌نیاز

- Windows 10 نسخه 1809 یا جدیدتر
- برای اجرای خروجی self-contained نیازی به نصب .NET نیست.
- برای Build از سورس: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

بستهٔ Windows x64 را از [آخرین انتشار GitHub](https://github.com/payeh/NobatQeshm/releases/latest) دانلود و از ZIP خارج کنید. سپس `NobatQeshm.Wpf.exe` را اجرا کنید. مشکل باز نشدن پنجرهٔ اصلی در نسخهٔ `1.0.1` رفع شده است.

## Build

```powershell
dotnet restore .\NobatQeshm.slnx
dotnet build .\NobatQeshm.slnx -c Release
```

## ساخت خروجی مستقل ویندوز

```powershell
dotnet publish .\src\NobatQeshm.Wpf\NobatQeshm.Wpf.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\release\win-x64
```

فایل اجرایی نهایی در مسیر زیر ساخته می‌شود:

```text
release\win-x64\NobatQeshm.Wpf.exe
```

## ساختار پروژه

```text
src/NobatQeshm.Wpf/
├── MainWindow.xaml          رابط اصلی و تمام تب‌ها
├── MainWindow.xaml.cs       ورود، نشست و پروفایل
├── MainWindow.Data.cs       رزرو، رسید، پلاک، نرخ و تردد
├── MainWindow.Monitor.cs    موتور پایش و هشدار
├── QeshmApi.cs              کلاینت API و پردازش تصویر
├── Storage.cs               ذخیره امن نشست و اطلاعات محلی
├── ReceiptService.cs        تولید و چاپ رسید HTML
├── Dialogs.cs               پنجره‌های مدیریت و ویرایش
└── Assets/icon.ico          آیکون اصلی برنامه
```

## امنیت و حریم خصوصی

- مقدار کوکی‌ها با DPAPI رمزگذاری می‌شود.
- داده‌ی حساس در گزارش عملیات چاپ نمی‌شود.
- PAN و کد ملی/موبایل در جزئیات تردد ماسک می‌شوند.
- هیچ رمز، OTP یا کوکی‌ای داخل مخزن ذخیره نشده است.
- برنامه فقط به `https://nobat.qeshm.ir` متصل می‌شود.

## قراردادهای اصلی API

| عملیات | متد و مسیر |
|---|---|
| دریافت کپچا | `GET /api/auth/captcha` |
| درخواست OTP | `POST /api/auth/signinVerify` |
| وضعیت نشست | `GET /api/auth/session` |
| مشخصات کاربر | `POST /api/private/profile/get-info` |
| لیست پلاک | `POST /api/private/pol-laft/plk` |
| ثبت پلاک | `PUT /api/private/pol-laft/plk` |
| ویرایش پلاک | `PATCH /api/private/pol-laft/plk` |
| حذف پلاک | `DELETE /api/private/pol-laft/plk` |
| نرخ خدمات | `POST /api/public/pol-laft/pol-prices` |
| تردد | `POST /api/private/pol-laft/pol-entry` |
| نوبت‌های من | `GET /api/private/pelak/reservation` |
| نوبت‌های آزاد | `POST /api/private/pelak/reservation` |
| ثبت نهایی نوبت | `PUT /api/private/pelak/reservation` |

## سازنده

Farhad Farhadi — `09361263005`

## مجوز و مسئولیت

این ابزار یک کلاینت مستقل برای ساده‌سازی کار با سامانه است. استفاده از آن باید مطابق قوانین و شرایط سامانه‌ی مقصد انجام شود.

