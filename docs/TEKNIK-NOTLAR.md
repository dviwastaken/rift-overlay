# RiftOverlay: teknik notlar

Bu dosya geliştirme sırasında tutulan ayrıntılı notlardır (tasarım kararları, doğrulanan API şemaları, bilinen sınırlar, yol haritası). Kısa tanıtım için [README](../README.md) dosyasına bak.


Overwolf'suz, kendi kendine yeten bir League of Legends oyun içi overlay'i. Oyunun kendi açtığı
**Live Client Data API**'sinden okuyup oyunun üstünde saydam, tıklamayı geçiren bir WPF penceresi
çiziyor.

Oyun process'ine hiç dokunmuyor: enjeksiyon yok, DirectX hook'u yok, `ReadProcessMemory` yok.
Yaptığı tek şey `https://127.0.0.1:2999` üzerinden HTTP okumak ve kendi penceresini çizmek —
Vanguard'ın engellediği yöntemlerin hiçbirini kullanmıyor.

Riot bu API'yi geliştirici dokümanında listeliyor ama aynı sayfada üçüncü parti uygulamalar için
**resmî destek vermediğini** ve dokümantasyon/uptime/değişiklik bildirimi garantisi olmadığını
yazıyor. Şema haber verilmeden değişebilir; `tools/LiveApiProbe` bunun için var ve her yamadan
sonra çalıştırılmalı.

## Çalıştırma

```bash
dotnet run --project src/RiftOverlay -- --demo
```

`--demo` senaryolu bir maçı oynatır; oyun açmadan yerleşimi ve sayaçları görmeni sağlar.
Gerçek kullanım için argümansız çalıştır:

```bash
dotnet run --project src/RiftOverlay
```

**Oyunun `Borderless` veya `Windowed` modda olması şart.** Exclusive fullscreen'de Windows hiçbir
yabancı pencereyi swap chain'in üstüne bindirmez — Overwolf'un tek gerçek teknik avantajı da budur.

## Kısayollar

| Kısayol | Etki |
|---|---|
| `Ctrl+Alt+1..5` | O sıradaki rakibin **Flash**'ını işaretle (tekrar bas → iptal) |
| `Ctrl+Alt+Shift+1..5` | Aynı rakibin diğer summoner spell'ini işaretle |
| `Ctrl+Alt+O` | Etkileşim modu — tıklama geçirgenliği kapanır, paneller sürüklenebilir olur |
| `Ctrl+Alt+H` | Overlay'i gizle / göster |

Numaralar rakip panelindeki sıralamayı takip eder. Flash hangi tuşta olursa olsun `Ctrl+Alt+N`
onu bulur — rakip Flash'ı ikinci slotta taşıyorsa da doğru sayaç başlar.

**Hepsi değiştirilebilir.** Etkileşim modundaki **KISAYOLLAR** bölümünde bir satıra tıklayıp yeni
kombinasyona basıyorsun; `Esc` iptal eder. Dört ayar var: etkileşim modu, gizle/göster, ve
1–5 tuşlarına uygulanan iki **değiştirici** (Flash ve diğer spell). Beş tuşu tek tek ayarlamak
yerine kombinasyonu bir kez seçiyorsun — çakışma da neredeyse her zaman kombinasyonda oluyor.

Kayıt sırasında iki incelik var. Yakalama başlarken **tüm global kısayollar geçici olarak
bırakılıyor**: kayıtlıyken Windows kombinasyonu pencereye değil kısayol işleyicisine gönderir,
yani yeniden atanmaya en çok ihtiyacı olan tuşlar tam da yakalanamayan tuşlar olurdu. Ayrıca
overlay normalde odak almayı reddediyor (oyunun ortasında odak çalmasın diye), bu yüzden odak
yalnızca yakalama süresince açılıyor.

Değiştiricisiz bir kısayol kabul edilmiyor: `Q` atanabilseydi oyunda Q'ya basmak yetenek yerine
sayaç başlatırdı.

Overlay yalnızca oyun penceresi ön plandayken çizilir; alt-tab yaptığında kendiliğinden kaybolur.

## Şu an gösterdikleri

- **Objektif sayaçları** — Dragon / Herald / Baron / Elder / void grub geri sayımı, takım skorları
- **Buff süreleri** — Baron ve Elder buff'ının kimde olduğu ve ne zaman biteceği
- **Inhibitor respawn** — düşen her inhibitor için ayrı sayaç, taraf ve koridor bilgisiyle
- **Dalga sayacı** — sonraki minyon dalgası ve top merminin kaç dalga sonra geleceği (varsayılan kapalı)
- **Hedef eşya** — gittiğin iteme kalan altın, doluluk çubuğuyla (aşağıya bak)
- **Rakip eşya** — rakipler önemli bir item bitirdiğinde çıkan güç sıçraması bildirimi
- **Koridor karşılaştırması** — koridor rakibinle CS ve seviye farkı (aşağıya bak)
- **Rakip respawn'ları** — her rakip için canlı/ölü durumu, kalan süre, KDA
- **Summoner spell takibi** — elle işaretlenen Flash/TP sayaçları (aşağıya bak)
- **Olay akışı** — son kill'ler, objektifler, kule ve inhibitor düşüşleri
- **Champion select** — rakip pickleri, yasaklar ve kalan süre; oyun daha açılmadan (aşağıya bak)

Geçici sayaçlar (buff'lar, düşen inhibitor'lar) süresi dolunca `hazır` yazıp durmak yerine sıradan
tamamen çıkıyor; kalıcı olanlar yeşile dönüp `hazır` yazıyor.

### Mod farkındalığı

Dragon, Baron, Herald, void grub ve dalga sayaçları yalnızca Summoner's Rift'te (`Map11`)
gösteriliyor. ARAM, Arena ve Nexus Blitz'te bu objektifler yok, dolayısıyla sayaçları da yok —
ama **inhibitor sayacı her modda kalıyor**, çünkü koridoru olan her haritada inhibitor var.
Rakip listesi, hedef eşya, spell sayaçları ve olay akışı da her modda çalışıyor.

Rift dışındaki modlarda durum panelinde modun adı yazıyor (`Bağlı · ARAM`), böylece eksik sayaçlar
bozukluk gibi görünmüyor.

Harita adı **boş** gelirse Rift varsayılıyor: veri gelmemesi başka bir modda olduğumuzun kanıtı
değil. Ama **tanınmayan** bir harita kimliği Rift sayılmıyor — yeni bir kimlik büyük ihtimalle
yeni bir mod, ve olmayan bir pit için Baron sayacı göstermek tam da bu kontrolün engellediği şey.

Sayaçlar 250 ms'lik polling arasında interpolasyonla akıtılıyor, yani geri sayım takılmadan iniyor.
Geri sayımlar yukarı yuvarlanır: 28 saniyelik bir respawn anında `00:27` değil `00:28` gösterir.

### Summoner spell sayaçları ve `~` işareti

Oyun rakip cooldown'larını hiçbir şekilde vermiyor, o yüzden sayacı **sen** başlatıyorsun. Ama
süresini tahmin etmiyoruz: rakibin envanterinde **Ionian Boots of Lucidity** varsa API'den okunup
hesaba katılıyor.

Cosmic Insight okunamıyor — API başka oyuncular için sadece keystone'u ve iki rün ağacının adını
veriyor, minör rünleri asla. Bu yüzden rakip Inspiration ağacı kullanıyorsa sayaç *Cosmic Insight
var* varsayıp en erken süreyi gösteriyor ve sonuna `~` koyuyor. `~` gördüğün yerde gerçek cooldown
gösterilenden uzun olabilir, kısa olamaz.

### Hedef eşya ve kalan altın

Kendi altının API'de tam olarak var (`activePlayer.currentGold`), ama **item fiyatları yok** — API
yalnızca sahip olduğun itemlerin fiyatını veriyor. Bu yüzden fiyatlar ve build ağacı Riot'ın
**Data Dragon** CDN'inden geliyor: ücretsiz, anahtarsız, yamaya göre sürümlü. Yama başına bir kez
indirilip `%LOCALAPPDATA%\RiftOverlay\ddragon\` altına yazılıyor; sonraki açılışlarda internet
olmasa da önbellekten okunuyor. Hiç indirilemezse panel gizli kalıyor ve uyarı kartında sebebi
yazıyor.

Katalog **`tr_TR`** ile indiriliyor, çünkü Live Client API item adlarını istemcinin diliyle
veriyor — canlı veride item `"Ebedi Kılıç"` olarak geliyor. İngilizce katalogla panel "Infinity
Edge" yazacaktı, yani oyunda gördüğün adla tutmayacaktı. Eşleştirme ID üzerinden yapıldığı için
dil yalnızca okunabilirliği etkiliyor, fiyatları değil. Dil `settings.json` içindeki `ItemLocale`
ile değiştirilebilir; istenen dil CDN'de yoksa `en_US`'e düşüyor.

Hedef item envanterindeki parçalardan çıkarılıyor: bir parçaya en çok altın yatırdığın bitmiş item
seçiliyor. Yatırılan altın hesaplanırken parça içindeki parçalar iki kez sayılmıyor — Sheen'in
değeri 900, "900 + içindeki Long Sword" değil.

İki aday eşit çıkarsa (tek başına bir Pickaxe dört iteme birden gidiyor) item adının yanına `?`
konuyor. Etkileşim modundaki **HEDEF EŞYA** listesinden hedefi elle seçebilirsin; `Otomatik`
çıkarıma geri döner. Elle seçim maç bitince sıfırlanıyor.

### Rakip eşya bildirimi

Rakip biri önemli bir item bitirdiğinde sağ üstte, rakip listesinin altında bir satır çıkıyor:
`22:25  Caitlyn → Üçlü Kuvvet`. En yenisi renkli, gerisi sönük — göz doğrudan az önce değişene
gidiyor.

"Önemli" tanımı **küratörlü bir liste değil**, itemin kendi fiyatı: 2500 altın ve üstü bitmiş
itemler bildiriliyor. Elle yazılmış bir "korkutucu itemler" listesi her yamada gözden geçirilmek
zorunda kalır ve sessizce eskir; katalog zaten her itemin fiyatını biliyor. Parçalar bildirilmiyor,
çünkü sürekli alınıyorlar — güç sıçraması bitmiş itemde oluyor.

Bir oyuncu **ilk görüldüğünde** envanteri yalnızca kaydediliyor, bildirilmiyor. Yoksa maç
ortasında overlay'i açtığında her rakip için altı tane geçmiş item bildirimi düşerdi.

### Koridor karşılaştırması

Kendi CS ve seviyenle koridor rakibininki yan yana, farkıyla birlikte:

```
KORİDOR  Ahri
CS      87   94   −7
Seviye  11   11    =
middle
```

Fark öndeysen yeşil, gerideysen kırmızı. Oyun içinde rakibin CS'ini görmek için Tab'a basıp skor
tablosunu açman gerekiyor — dövüş sırasında yapılacak şey değil. Sürekli görünen bir fark
göstergesi "dalabilir miyim, dalgayı itmeli miyim" sorusunu bakmadan cevaplıyor.

**Rakip nasıl bulunuyor:** önce `position` alanı üzerinden — sende `MIDDLE` yazıyorsa rakiplerden
`MIDDLE` olanı eşleşir. Ama canlı yakalamada botların pozisyonu doluyken **insan oyuncunun
pozisyonu `NONE` geldi**, yani otomatik eşleşme her zaman mümkün değil. Bu yüzden etkileşim
modundaki **KORİDOR RAKİBİ** listesinden elle de seçebiliyorsun; `Otomatik` pozisyona geri döner.
Elle seçim maç bitince sıfırlanıyor.

Pozisyon boşsa ve elle seçim de yapılmadıysa panel hiç görünmüyor — yanlış rakiple karşılaştırma
göstermektense hiç göstermemek doğru.

### Uyarılar

Overlay bir şeyi yapamadığında sessiz kalmak yerine sol üstte sarı bir kart gösteriyor. Şu an üç
durum bildiriliyor:

- **Kısayol alınamadı** — Windows bir kısayolu ilk isteyene veriyor, yani başka bir uygulama
  `Ctrl+Alt+3`'ü çoktan almışsa bizimki hiç çalışmaz. Kart hangi kombinasyonların alınamadığını
  tek tek yazıyor.
- **Item kataloğu indirilemedi** — hedef eşya paneli çalışmayacak demektir.
- **Exclusive fullscreen** — oyun tam ekran modundaysa overlay hiç çizilemez. Burada bir incelik
  var: uyarı da oyunun üstünde görünemeyeceği için, bu durum tespit edildiğinde overlay oyun ön
  planda değilken de görünür kalıyor. Yani alt-tab yaptığında uyarıyı okuyabiliyorsun.

Bunlar uygulama düzeyinde sorunlar, maç durumu değil; maç bitince kaybolmuyorlar.

### Log

```
%LOCALAPPDATA%\RiftOverlay\logs\riftoverlay.log
```

Uygulama başlangıcı, maça bağlanma/kopma, katalog indirme sonucu ve yakalanan her istisna buraya
yazılıyor. UTF-8 BOM ile yazılıyor ki Türkçe karakterler her araçta doğru okunsun. Dosya 1 MB'ı
geçince `.1` uzantısıyla arşivleniyor, bir önceki dosya saklanıyor.

Log **olaylar** için, telemetri için değil: her yazım dosyayı açıp kapatıyor. Oturum başına birkaç
düzine satır için doğru, 250 ms'lik polling döngüsü için yanlış — o yüzden sık tekrarlayan şeyler
(maç yokken saniyede bir reddedilen bağlantı gibi) tek tek değil, yalnızca **durum değiştiğinde**
yazılıyor.

## Champion select ve rünler (LCU)

Oyun içi API'nin yanında ikinci bir kaynak var: **LCU**, yani League istemcisinin kendi yerel
API'si. Oyun kapalıyken çalışıyor — champion select tam da orada geçiyor.

Bağlantı, istemcinin çalışırken yazdığı **lockfile**'dan kuruluyor: port ve parola orada duruyor,
ikisi de istemci her yeniden başladığında değişiyor. Lockfile sabit bir kurulum yolunda aranmıyor,
**çalışan process'in kendi konumundan** bulunuyor — istemci başka bir sürücüde olabilir.

Şampiyon adları da istemciden geliyor (`champion-summary.json`), yani her zaman onun yaması ve
diliyle uyumlu; ayrıca indirilecek bir şey yok.

### Rün ön ayarları

Öneri kaynağı **bilerek yok**. Bir build sitesini kazımak hem kırılgan olurdu hem de kişisel bir
araca başkasının kullanım şartlarını sokardı. Bunun yerine: rün sayfanı bir kez kendi istediğin gibi
kurup **"Bu şampiyon için rünleri kaydet"** diyorsun, o şampiyonu bir daha seçtiğinde geri yazılıyor.

İki koruma var:

- **Otomatik uygulama varsayılan kapalı.** Uygulamak bir rün sayfasını siliyor ve yeniden yazıyor;
  buradan geri alınamıyor.
- **Yalnızca bu aracın oluşturduğu sayfa siliniyor.** Hesaplarda rün sayfası hakkı çok az, yani
  yer açmak gerekiyor — ama kendi kurduğun bir sayfayı silip yerine bizimkini koymak savunulamaz.

Otomatik uygulama, şampiyon **kilitlendiğinde** çalışıyor; hover'da değil. Hover değişebilir, her
hover'da sayfayı yeniden yazmak seçim boyunca rünlerini çalkalardı.

## Panelleri taşıma

Etkileşim modunda (`Ctrl+Alt+O`) panelleri **sürükleyerek** istediğin yere taşıyabilirsin. Konumlar
anında kaydediliyor; ayar kartındaki **Konumları sıfırla** hepsini varsayılana döndürüyor.

Konumlar piksel olarak değil, **ekranın oranı olarak** saklanıyor. Piksel saklansaydı 2560 genişlikte
kenara koyduğun panel, 1600 genişlikte ekranın ortasında kalırdı. Oranla her çözünürlükte aynı yere
denk geliyor. Ayrıca panel her zaman ekran içinde kalacak şekilde kırpılıyor — dışarı taşan bir
paneli geri sürüklemek mümkün olmazdı.

Bir davranış değişikliği: paneller artık birbirine bağlı değil. Önceden sol sütun dikey bir yığındı
ve üstteki panel büyüyünce alttaki kendiliğinden kayıyordu; serbest konumlandırma bunu yapamıyor.
Bu yüzden uyarı kartının varsayılan yeri üst ortaya alındı — eskiden objektif sırasını aşağı itiyordu.

## Ayarlar

`Ctrl+Alt+O` ile etkileşim moduna geçince sağ altta ayar kartı çıkıyor: her panel ve her objektif
sayacı ayrı ayrı kapatılabiliyor, opaklık ayarlanabiliyor. Değişiklik anında uygulanıyor ve anında
diske yazılıyor:

```
%LOCALAPPDATA%\RiftOverlay\settings.json
```

Dosya bozulursa ya da elle kurcalanıp geçersiz hale gelirse overlay varsayılanlarla açılır —
ayar dosyası yüzünden başlamama diye bir durum yok. Opaklık 0.3'ün altına inemez.

## Mimari

| Dosya | İş |
|---|---|
| `src/RiftOverlay/Services/LiveClientService.cs` | Live Client Data API polling'i, bağlantı durumu |
| `src/RiftOverlay/Services/DemoDataSource.cs` | Senaryolu sahte maç (`--demo`) |
| `src/RiftOverlay/Services/ObjectiveTracker.cs` | Olay akışından objektif geri sayımı üretimi |
| `src/RiftOverlay/Services/CooldownTracker.cs` | Summoner spell sayaçları ve haste hesabı |
| `src/RiftOverlay/Services/ItemCatalog.cs` | Data Dragon item kataloğu, indirme ve önbellek |
| `src/RiftOverlay/Services/BuildTargetTracker.cs` | Hedef item çıkarımı ve kalan altın hesabı |
| `src/RiftOverlay/Services/GameWindowTracker.cs` | Oyun penceresini bulma, ekran dikdörtgeni |
| `src/RiftOverlay/Services/SettingsService.cs` | Ayarların diske yazılması ve okunması |
| `src/RiftOverlay/Interop/NativeMethods.cs` | Layered / transparent / topmost pencere ayarları, global hotkey |
| `src/RiftOverlay/ViewModels/OverlayViewModel.cs` | Snapshot → görünüm durumu |
| `tools/LiveApiProbe/` | Gerçek API'yi örnekleyip modellerle karşılaştıran teşhis aracı |
| `tests/RiftOverlay.Tests/` | Saf mantık için birim testleri (`dotnet test`) |

### Teşhis ve test

```bash
dotnet test
```

```bash
dotnet run --project tools/LiveApiProbe -- --seconds 60
```

Probe maç başlayana kadar bekler, 60 saniye örnekler, ham JSON'u diske yazar ve şu üç soruyu
cevaplar: alan adları modellerle tutuyor mu, hangi event isimleri işlenmiyor, `respawnTimer`
beklenen biçimde mi geliyor.

## Bilinen sınırlar

- **Objektif spawn süreleri patch'e bağlı.** `ObjectiveSettings` içindeki değerlerin tamamı (spawn
  ve respawn süreleri, buff süreleri, minyon dalgası ve top mermi sıklığı) yama notlarına göre
  doğrulanmalı. Kill event'inden türeyen *respawn* ve *buff* süreleri kesin; ilk spawn süreleri ve
  dalga hesabı bu konfigürasyon kadar doğru.
- **Koridorlar sıfır tabanlı numaralı, harfli değil:** `L0` alt, `L1` orta, `L2` üst. Harf taraf
  (left/center/right) belirtmiyor ve `L3` diye bir koridor yok — üçünü de canlı yakalamayla
  gördük. Aradaki tuzak şu: `L1` ve `L2` bilindikten sonra "geriye `L3` kalıyor, o da alttır"
  çıkarımı doğal görünüyor ama yanlış; alt koridor `Inhib_TChaos_L0_P1_...` olarak geldi. Aralık
  dışında bir numara gelirse koridor adı yazılmıyor, taraf bilgisi görünmeye devam ediyor.
- **Kuleler inhibitorlarla aynı numaralandırmayı kullanıyor** (`Turret_TChaos_L2_P3_...`), yani tek
  bir kimlik çözümleyici ikisine birden yetiyor. `P` numarası üsse yaklaştıkça küçülüyor: dış kule
  `P3`, iç kule `P2`, inhibitor kulesi ve inhibitorun kendisi `P1`.

### Event adları nereden doğrulandı

Live Client Data API'nin event adları hiçbir yerde resmî olarak listelenmiyor. Tam liste, kurulu
oyunun kendi çalıştırılabilir dosyasındaki string tablosundan okundu — event adları orada payload
alan adlarıyla (`VictimName`, `KillStreak`, `DragonType`, `AcingTeam`, `Assisters`) yan yana duruyor,
ki bu tablonun API şeması olduğunu kesinleştiriyor:

```
GameStart · GameEnd · MinionsSpawning · ChampionKill · Multikill · FirstBlood · Ace
DragonKill · HordeKill · HeraldKill · BaronKill
TurretKilled · FirstBrick · InhibKilled · InhibRespawningSoon · InhibRespawned
```

Hepsi işleniyor. `HordeKill` (void grub) bu yolla doğrulandı; öncesinde tahmindi ve yanlış olsaydı
grub sayacı sonsuza kadar sessizce sıfırda kalırdı.

**Atakhan bu tabloda yok.** Binary'de `AtakhanKill` dizesi geçiyor ama bir arayüz stringinin
(`Interactive3DRevealEnemyAtakhanKill`) parçası olarak; API event adı değil.

### Yapı olaylarında alan adı tuzağı

Her inhibitor olayı yapı kimliğini **olayın kendi adıyla aynı** alanda taşıyor:

```json
{"EventName": "InhibKilled",    "InhibKilled":    "Inhib_TChaos_L1_P1_..."}
{"EventName": "InhibRespawned", "InhibRespawned": "Inhib_TChaos_L1_P1_..."}
```

Yani `InhibRespawned` olayında `InhibKilled` alanı **yok**. Üçü de ayrı modelleniyor; tek alan
varsayımı, geri gelen inhibitor'ı sıradan erken kaldıran kodu sessizce devre dışı bırakmıştı.

Bir de öldüren her zaman oyuncu değil: canlı veride bir kuleyi minyon yıkmıştı
(`KillerName: "Minion_T100L1S19N0121"`). İsim eşleşmezse takım ataması yapılmıyor, kod düşmüyor.

### İsim alanları hakkında

Canlı veride oyuncunun **her** isim alanı tam Riot ID taşıyor: `summonerName` bile `"Ornek#0001"`
biçiminde geliyor. Buna karşılık event'ler oyuncuyu çıplak adıyla anıyor (`KillerName: "Ornek"`).
İkisini doğrudan karşılaştırmak hiçbir zaman eşleşmez — bu yüzden isim eşleştirmesi her iki tarafı
da etiketten önceki kısma indirgiyor. Bu tuzağa bir kez düşüldü ve takım atfı (dragon, void grub,
buff sahibi) sessizce hiç çalışmadı.

Dahası, olayların hangi alanı kullandığına dair **tek bir kural yok**. Aynı olayın içinde iki
biçim birden görülebiliyor:

```json
{"KillerName": "Sion Bot", "VictimName": "Oyuncu", "Assisters": ["Akali Bot"]}
```

Botlar `summonerName` biçiminde (`"Sion Bot"`), insanlar `riotIdGameName` biçiminde (`"Oyuncu"`)
anılıyor. Bu yüzden eşleştirme üç alanı da deniyor; tek alana bağlansaydı botların aldığı
objektifler hiçbir takıma yazılmazdı.
- **Rakip summoner spell cooldown'ları ölçülemez.** Elle işaretleme + hesaplama; Cosmic Insight
  görünmediği için Inspiration ağacı kullanan rakiplerde sayaç alt sınırdır (`~`).
- **Champion select verisi ayrı kaynakta.** Rün/build önerisi için LCU API'ye (lockfile + WebSocket)
  bağlanmak gerekiyor; bu sürümde yok.
- Çok monitörlü ve karışık DPI'lı kurulumlarda oyun başka bir monitöre taşınırsa yerleşim bir
  sonraki 500 ms'lik geçişte düzeliyor.

## Sonraki adımlar

**Dört fazın dördü de tamamlandı:** oyun içi overlay, cila ve ayarlar, LCU (champion select ve rün
yazma), maç kaydı ve kişisel istatistikler. Doğrulanmamış eşleme kalmadı — event adları oyunun
binary'sinden okundu, koridor numaraları (`L0` alt, `L1` orta, `L2` üst) canlı Rift turlarında
teyit edildi.

Üstüne bir **client** eklendi: tray ikonundan açılan, oyun dışında yaşayan normal bir pencere.
Overlay tıklama geçirgen ana yüzey olarak duruyor; client oyuncu arama ile rün ve build aktarmanın
yeri. Çıkış artık yalnızca tray menüsünden.

**Build verisi kaynağı:** OP.GG'nin kendi yayınladığı MCP sunucusu (`mcp-api.op.gg/mcp`) —
anahtarsız, Türkçe isim döndürüyor ve her satırda örneklem sayısı taşıyor. Denenen diğer bütün
siteler kapalı çıktı; Riot'ın kendi öneri alanları (`recommended`, `recommendedItemDefaults`) hâlâ
duruyor ama **boş**. Şema haber vermeden değişebileceği için doğrulama aracına bir mod eklendi:
`dotnet run --project tools/LiveApiProbe -- --opgg --champion Orianna --position mid`.

**Panelin değişmez kuralı:** bir build önerisi hiçbir zaman örneklem sayısı olmadan gösterilmiyor.
Ölçtüğümüz bir challenger build'i 6 maça dayanıyordu; rakamsız gösterilseydi %83 kazanan bir öneri
gibi okunurdu.

Client'ın **Build** sekmesi çalışıyor: şampiyon ve koridor girince başlangıç eşyaları, çizme,
çekirdek build, sihirdar büyüleri, iki rün sayfası, yetenek ve maks sırası geliyor. **Rün sayfası
tek tıkla istemciye yazılabiliyor**; ne yazılacağı düğmenin yanında duruyor, çünkü uygulamak bir
sayfayı silip yeniden kuruyor ve geri alma yok. Silme yalnızca `RiftOverlay` adlı sayfalarla
sınırlı — kullanıcının kendi sayfalarına dokunulmuyor. Champion select'te bir şampiyon kilitlenince
client **zaten açıksa** build kendiliğinden yükleniyor; kapalıysa pencere zorla öne getirilmiyor,
çünkü seçim süreliyken odak çalmak faydadan çok zarar.

**Eşya seti yazma da çalışıyor ve canlı doğrulandı.** Bu uç hesabın **tüm set koleksiyonunu tek
istekte** değiştirdiği için yazma her zaman oku-düzenle-yaz; yalnızca `RiftOverlay` önekli setler
kaldırılıyor, başka araçların setlerine dokunulmuyor. Doğrulama kuşatmalı: `--write-itemset` önce
her setin uid'ini kaydediyor, yazıyor, sonra hepsinin durduğunu tek tek kontrol ediyor.

Buradan çıkan ders koda ve teste yazıldı: istekler bir ara **seçeneksiz** serileştiriliyordu, yani
özellik adları PascalCase gidiyordu. İstemci böyle bir gövdeye 200 dönüp yalnızca tanıdığı alanları
saklıyor — ve koleksiyonun tamamı değiştiği için tek bir yazma, kullanıcının mevcut bir setinin
başlığını ve bloklarını sildi. Artık tek bir paylaşılan `LcuJson.Options` var ve yedi koruma testi
**gerçek yazma seçenekleriyle** çalışıyor; kendi kopyasını kuran bir doğrulama tam da bu hatayı
kaçırmıştı.

**Oyuncu arama** Riot ID ile çalışıyor: rank, son maçlar, şampiyon ve KDA. Tamamen LCU üzerinden,
**Riot Web API anahtarı gerekmiyor** — üç ucun da başkasının puuid'inde yanıt verdiği arayüz
yazılmadan önce denendi. Riot ID çözümü `alias/lookup` ile; eski isim tabanlı uç artık 422 veriyor.

Burada da bir tuzak var ve rakamı sessizce uyduruyordu: **istemci `losses` alanını dolduruyor gibi
görünüp hep sıfır bırakıyor.** Silver I bir hesap 163 galibiyet / 0 mağlubiyet döndürdü, yani
galibiyet oranı formülü her dereceli oyuncu için %100 üretiyordu. Artık oran yalnızca en az bir
mağlubiyet bildirildiğinde gösteriliyor; aksi hâlde sadece galibiyet sayısı yazıyor. Gerçekten
yenilmemiş bir hesabın oranı da bu yüzden gizleniyor — doğru bir şeyi saklamak geri alınabilir,
yanlış bir şey basmak değil.

Client'ta üç bölüm var:

- **Oyuncu** — Riot ID ile arama; rank, son maçlar, şampiyon, KDA, CS, süre ve mod.
- **Build** — OP.GG önerisi; eşyalar görselleriyle, rünler, yetenek sırası, iyi ve zor gittiği
  eşleşmeler. Rün sayfası ve eşya seti tek tıkla istemciye yazılabiliyor.
- **İstatistikler** — bu aracın kendi kaydettiği maçlar: kazanma oranı, ortalama KDA, vision,
  şampiyon bazında CS@10. Rapordaki "kopyalanamayacak tek özellik" bu tarafta; OP.GG bir build'in
  genelde iyi olduğunu söyleyebilir, yalnızca yerel kayıt onun *sende* nasıl gittiğini söyler.

Karşı eşleşmelerde gösterilen oran **daima bizim oranımız**. Sunucu `weak_counters` içinde düz
`win_rate` alanını karşı tarafa ait veriyor; `my_win_rate` ise iki listede de aynı tarafı
gösterdiği için o kullanılıyor. Aynı yerde bir tuzak daha var ve şema doğrudan onu doğuruyor:
**sunucu alanları istenen sırada değil, kendi sırasında bildiriyor** — bu yüzden parser sınıf
tanımlarını okumak zorunda, ve iki karşı eşleşme listesi de tel üzerinde aynı sınıf adını taşıyor.

Sırada kalan tek şey **daha hafif yoklama**: `allgamedata` 250 ms'de bir çekiliyor. "Böl" tavsiyesi
burada işe yaramıyor çünkü build paneli `activePlayer.currentGold`'a bağlı ve bölmek isteği üçe
çıkarır. Gerçek kazanç frekansı düşürmek olurdu (arayüz zaten anlık görüntüler arasında ekstrapole
ediyor) ama bedeli olay algısında gecikme, ve yükü oyun açıkken ölçmeden karar verilmeyecek.

## Maç kaydı ve istatistikler

Her maç `%LOCALAPPDATA%\RiftOverlay\matches.db` içine SQLite olarak yazılıyor: maçın kendisi
(şampiyon, koridor, süre, KDA, vision, sonuç), tam olay akışı ve 15 saniyede bir alınan istatistik
örneği. Overlay zaten saniyede dört kez veri çektiği için kayıt ayrı bir yoklama yapmıyor, aynı
akışa biniyor. `--demo` modunda kayıt kapalı: senaryo verisi geçmişe gerçek maçmış gibi girmemeli.

Panel (`Geçmiş`) son maçları ve en çok oynanan üç şampiyonun özetini gösteriyor. Üç ayrıntı
bilerek böyle:

- **Sonucu görülmemiş maç ne galibiyet ne mağlubiyet sayılıyor.** Oyun çökerse ya da overlay maç
  ortasında kapanırsa satır kapanıyor ama sonuç boş kalıyor. Tahmin etmek kazanma oranını sessizce
  bozardı; `DecidedMatches` bu yüzden toplam maç sayısından ayrı tutuluyor.
- **Hiç sonucu olmayan şampiyonun oranı `—`, `%0` değil.** `%0` maçların nasıl gittiğine dair bir
  iddiadır; `—` bilinmediğini söyler.
- **CS@10 yalnızca 10. dakikaya ulaşmış maçlardan alınıyor.** 8. dakikada biten bir maçın CS'i,
  10 dakikalık veriymiş gibi ortalamaya girerse rakamı sessizce aşağı çeker.

Olaylar `(MatchId, EventId)` üzerinde tekil indeksle ve `INSERT OR IGNORE` ile yazılıyor. Canlı
olay akışı kümülatif — her yoklama maçın tüm olaylarını yeniden döndürüyor — bu yüzden ikinci kez
yazılan bir olayın hiçbir şey yapmaması bir düzen tercihi değil, doğruluk şartı.

`GameEnd` olayının üç değeri (`Win` / `Lose` / `Neither`) oyun ikilisindeki şema tablosundan
doğrulandı; **alan adı doğrulanmadı** — tabloda `Result` diye bir string yok ve bu projede her yapı
olayı verisini kendi adını taşıyan alanda gönderiyor. Bu yüzden hem `Result` hem `GameEnd` alanı
okunuyor, hangisi doluysa o alınıyor. Gerçek bir maç bittiğinde hangisinin geldiği loga yazılıyor.

Champion select oturumunun şeması 2026-09-02'de canlı olarak doğrulandı: takım dizileri, `actions`,
milisaniyelik `timer` bloğu ve en kritik ayrım olan "hover mı, kilit mi" (`championPickIntent` dolu
iken `championId` sıfır) modelle birebir tutuyor. Aynı gün üç koridorun da yapı kimlikleri
yakalandı, yani bilinen açık bir eşleme kalmadı.
