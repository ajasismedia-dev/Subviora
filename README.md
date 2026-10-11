# Subviora

**Oyunlardaki İngilizce altyazıları gerçek zamanlı olarak Türkçeye çeviren Windows uygulaması.**

Subviora oyun ekranını okur (OCR), Akıllı Arayüz Algılama ile gerçek altyazıları menü ve gösterge
metinlerinden ayırır, metni çevrimdışı bir çeviri motoruyla Türkçeye çevirir ve sonucu oyunun üzerinde
yerel bir altyazı katmanıyla gösterir.

İlk hedef oyun: **GTA V Hikaye Modu**. Uzun vadeli hedef: Windows oyunlarıyla genel uyumluluk ve Steam
üzerinden dağıtım.

> [!IMPORTANT]
> **Geliştirme durumu: AG-02 — Ekran Yakalama ve OCR Değerlendirmesi.**
> Bu sürümde çalışan oyun pencerelerinin güvenli tespiti, Windows Graphics Capture (WGC) ve Win32 BitBlt
> ile ekran yakalama, OCR motoru mimarisi (Windows OCR, Tesseract 5.2, PaddleOCR değerlendirmesi) ve
> karşılaştırmalı test (benchmark) sistemi uygulanmıştır. Çeviri motoru AG-03 aşamasında entegre edilecektir.

## Bu sürümde neler var

| Özellik | Durum |
|---|---|
| WPF masaüstü arayüzü (koyu tema, Türkçe) | ✅ Çalışıyor |
| Uygulama yaşam döngüsü, bağımlılık enjeksiyonu, tek örnek koruması | ✅ Çalışıyor |
| Yapılandırma yükleme ve doğrulama | ✅ Çalışıyor |
| Yapılandırılmış günlük kaydı (JSON, dönen dosyalar) | ✅ Çalışıyor |
| Bildirim alanı (tepsi) simgesi | ✅ Çalışıyor |
| Oyun profili şeması (v1), katmanlı geçersiz kılma, Evrensel Mod (Universal) | ✅ Çalışıyor |
| GTA V Hikaye Modu profil eşleştirme | ✅ Çalışıyor |
| Windows oyun ve pencere keşif servisi (Win32, bellek/enjeksiyon yok) | ✅ Çalışıyor |
| Windows Graphics Capture (WGC) ekran yakalama (DPI ve yeniden boyutlandırma uyumlu) | ✅ Çalışıyor |
| Win32 GDI / BitBlt ekran yakalama yedek sağlayıcısı | ✅ Çalışıyor |
| Sınırlı (bounded) yakalama boru hattı ve bayat kare atma (frame dropping) | ✅ Çalışıyor |
| Yakalama ve OCR telemetrisi (FPS, çözünürlük, gecikme, bellek, kare atma) | ✅ Çalışıyor |
| İsteğe bağlı yerel teşhis ekran görüntüsü kaydetme | ✅ Çalışıyor |
| Değiştirilebilir OCR motoru mimarisi (`IOcrProvider`) | ✅ Çalışıyor |
| Windows Media OCR (WinRT, yerleşik, 0 MB, SAC uyumlu) | ✅ Çalışıyor |
| Tesseract 5.2 OCR sağlayıcısı (Apache-2.0, SAC uyumlu yerel x64 ikililer) | ✅ Çalışıyor |
| PaddleOCR karşılaştırmalı değerlendirme adaptörü ve belgelendirmesi | ✅ Çalışıyor |
| OCR Benchmark Test Koşucusu (CER, WER, Bounding Box IoU, P50/P95 gecikme) | ✅ Çalışıyor |
| Masaüstü Yakalama ve OCR Tanılama / Önizleme ekranı | ✅ Çalışıyor |
| Akıllı Arayüz Algılama (Smart UI Detection) | ❌ Gelecek aşama |
| Çevrimdışı / çevrimiçi Türkçe çeviri | ❌ AG-03 aşamasında planlandı |
| Doğrudan oyun içi altyazı katmanı (Win32 + Direct2D/DirectWrite) | ❌ AG-04/05 aşamasında planlandı |

## Sistem gereksinimleri

- Windows 10 (sürüm 2004 / derleme 19041 veya üzeri) ya da Windows 11, x64
- [.NET 10 Masaüstü Çalışma Zamanı](https://dotnet.microsoft.com/download/dotnet/10.0) (Windows Desktop Runtime)
- Yönetici izni **gerekmez**

## Derleme ve çalıştırma

Gereken: **.NET 10 SDK** (10.0.100 veya üzeri). Ayrıntılar için [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

```powershell
dotnet restore Subviora.sln
dotnet build Subviora.sln -c Release
dotnet test --solution Subviora.sln -c Release
dotnet run --project src/Subviora.Desktop -c Release
```

> [!NOTE]
> **Akıllı Uygulama Denetimi (Smart App Control)** açık olan Windows 11 sistemlerinde, imzasız geliştirme
> derlemelerinin DLL dosyaları engellenebilir. Uygulama bu durumda açıklayıcı bir hata mesajı gösterir.
> Resmî sürümler dijital olarak imzalanacaktır. Bkz. [docs/DEVELOPMENT.md](docs/DEVELOPMENT.md).

## Veri konumları

Subviora çalışırken yazdığı her şeyi kullanıcıya özel klasörde tutar; kurulum klasörü salt okunurdur.

| Konum | İçerik |
|---|---|
| `%LOCALAPPDATA%\Subviora\Logs` | Günlük dosyaları (`subviora-YYYYMMDD.log`, JSON) |
| `%LOCALAPPDATA%\Subviora\Config\settings.json` | İsteğe bağlı kullanıcı ayarları |
| `%LOCALAPPDATA%\Subviora\Profiles` | Kullanıcı oyun profilleri ve geçersiz kılmalar |
| `%LOCALAPPDATA%\Subviora\Cache` | Gelecekteki çeviri önbelleği için ayrıldı |

## Belgeler

- [Mimari](docs/ARCHITECTURE.md)
- [Geliştirme](docs/DEVELOPMENT.md)
- [Yol haritası](docs/ROADMAP.md)
- [Kararlar](docs/DECISIONS.md)
- [Oyun profili şeması](profiles/schema/game-profile.v1.schema.json)

## Lisans

Tescilli ticari yazılım. Tüm hakları saklıdır. Kullanılan üçüncü taraf bileşenler ticari kullanıma uygun
MIT / Apache-2.0 lisanslıdır (bkz. [docs/DECISIONS.md](docs/DECISIONS.md)).
