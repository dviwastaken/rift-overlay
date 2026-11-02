# RiftOverlay

Overwolf'suz, kendi kendine yeten bir **League of Legends oyun içi overlay'i**. Oyunun kendi açtığı
Live Client Data API'sinden okur ve oyunun üstüne saydam, tıklamayı geçiren bir WPF penceresi çizer.

> **English summary.** A standalone in-game overlay for League of Legends (C# / .NET 10 / WPF). It only
> reads the game's local Live Client Data API (`127.0.0.1:2999`) and the League client's local LCU API,
> then draws a transparent click-through window: objective timers, enemy respawns, summoner-spell
> tracking, item/gold targets, lane comparison, champion-select info and a local match history.
> No injection, no DirectX hooks, no memory reading. The UI and documentation are in Turkish.
> Not endorsed by Riot Games.

## Özellikler

- **Objektif sayaçları:** Dragon, Herald, Baron, Elder ve void grub geri sayımı; Baron/Elder buff süreleri
- **Inhibitor respawn** sayaçları (taraf ve koridor bilgisiyle) ve minyon dalgası sayacı
- **Rakip takibi:** canlı/ölü durumu, kalan respawn süresi, KDA; elle işaretlenen Flash/TP sayaçları
- **Eşya:** hedef eşyaya kalan altın ve rakip önemli bir eşya bitirdiğinde güç sıçraması bildirimi
- **Koridor karşılaştırması:** koridor rakibinle CS ve seviye farkı
- **Olay akışı:** kill'ler, objektifler, kule ve inhibitor düşüşleri
- **Champion select (LCU):** rakip pickleri, yasaklar, kalan süre ve rün ön ayarları
- **Maç kaydı:** oynanan maçlar yerel bir SQLite dosyasına yazılır, son maçlar ve şampiyon özetleri görünür
- Kısayollar, panel yerleşimi ve opaklık ayarlanabilir

Ayrıntılar için [docs/TEKNIK-NOTLAR.md](docs/TEKNIK-NOTLAR.md).

## Güvenlik ve kurallar

Oyun process'ine dokunmaz: enjeksiyon yok, DirectX hook'u yok, `ReadProcessMemory` yok. Yaptığı tek şey
`https://127.0.0.1:2999` üzerinden HTTP okumak, League istemcisinin yerel (LCU) API'sine bağlanmak ve kendi
penceresini çizmektir.

- Riot, Live Client Data API'yi dokümante eder ama üçüncü parti uygulamalar için **resmî destek vermez**;
  şema haber verilmeden değişebilir. Bu yüzden `tools/LiveApiProbe` var, her yamadan sonra çalıştırılmalı.
- `tools/LiveApiProbe` içindeki OP.GG kod yolu, OP.GG'nin yayınladığı **resmî olmayan** bir MCP uç noktasını
  kullanır. Kullanım koşullarını kendin kontrol et.
- Bu proje Riot Games tarafından onaylanmamıştır ve Riot Games'in görüşlerini yansıtmaz. *League of Legends*
  Riot Games, Inc.'in tescilli markasıdır.

## Gereksinimler

- Windows 10/11
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- League of Legends, **Borderless** veya **Windowed** modda (exclusive fullscreen'de Windows üstüne
  pencere bindirmez)

## Çalıştırma

```bash
# Oyun açmadan senaryolu bir demo maç
dotnet run --project src/RiftOverlay -- --demo

# Gerçek kullanım (oyun açıkken)
dotnet run --project src/RiftOverlay
```

| Kısayol | Etki |
|---|---|
| `Ctrl+Alt+1..5` | O sıradaki rakibin Flash'ını işaretle (tekrar bas: iptal) |
| `Ctrl+Alt+Shift+1..5` | Aynı rakibin diğer summoner spell'ini işaretle |
| `Ctrl+Alt+O` | Etkileşim modu: paneller sürüklenir, ayarlar açılır |
| `Ctrl+Alt+H` | Overlay'i gizle / göster |

Kısayollar etkileşim modundaki **KISAYOLLAR** bölümünden değiştirilebilir. Overlay yalnızca oyun penceresi
ön plandayken çizilir.

## Testler

```bash
dotnet test
```

Birim testleri yalnızca saf mantığı sınar, oyun ya da internet gerekmez. Gerçek API şemasını
modellerle karşılaştırmak için maç sırasında:

```bash
dotnet run --project tools/LiveApiProbe -- --seconds 60
```

## Yerel veriler

Ayarlar ve maç geçmişi senin bilgisayarında kalır, hiçbir yere gönderilmez:

```
%LOCALAPPDATA%\RiftOverlay\settings.json
%LOCALAPPDATA%\RiftOverlay\matches.db
```

Şampiyon ve eşya verisi Riot'un herkese açık Data Dragon servisinden indirilip önbelleğe alınır.

## Proje yapısı

| Klasör | İçerik |
|---|---|
| `src/RiftOverlay/Services/` | Live Client polling, objektif/cooldown/eşya izleyicileri, LCU istemcisi, ayarlar |
| `src/RiftOverlay/ViewModels/` | Veri anlık görüntüsünden görünüm durumu |
| `src/RiftOverlay/Interop/` | Saydam / tıklama geçiren pencere ve global kısayol çağrıları |
| `tests/RiftOverlay.Tests/` | xUnit birim testleri |
| `tools/LiveApiProbe/` | Gerçek API'yi örnekleyip modellerle karşılaştıran teşhis aracı |
| `docs/` | Teknik notlar, doğrulanan şemalar, bilinen sınırlar |

## Lisans

[MIT](LICENSE)
