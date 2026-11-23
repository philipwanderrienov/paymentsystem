# PaymentHub Simple Victoria RTGS

Versi sederhana berdasarkan penelusuran kode Victoria di `D:/Bank-Victoria`.
Semua project berada dalam **satu solution**: `PaymentHub.Simple.Victoria.sln`.
Gateway merupakan satu executable ASP.NET Core; module lain dijalankan dalam proses
tersebut agar demo cukup memakai satu perintah.

## Menjalankan

Prasyarat: .NET SDK 10.0.400 atau patch lebih baru dalam feature band yang sama.
Tidak memerlukan NuGet package eksternal, database, Redis, FTP, atau koneksi bank.

```powershell
./scripts/run.ps1
```

Endpoint: http://localhost:5080/health
Hentikan dengan Ctrl+C. Buka solution di Visual Studio untuk melihat semua project.
Perintah langsung juga tersedia:

```powershell
dotnet run --project src/PaymentHub.Simple.Gateway
```

Folder data relatif terhadap working directory. Script run memakai root solution.
Konfigurasi ada di `src/PaymentHub.Simple.Gateway/appsettings.json` dan dapat dioverride
melalui environment `Demo__DataDirectory`, `Demo__WorkerEnabled`, dan seterusnya.

## Project dalam solution

| Project (prefix PaymentHub.Simple.) | Tanggung jawab |
| --- | --- |
| Gateway | Startup, dependency injection, pintu masuk HTTP |
| Microservice | Health dan konfigurasi demo |
| Microservice.Rtgs | Endpoint transaksi RTGS dan pemrosesan manual |
| Adapter.Rtgs | Background worker Victoria: baca file, import STPG, proses CBS |
| Service | Alur status, validasi VA, settlement, simulator Victoria |
| Library | Konfigurasi dan lokasi folder |
| Library.Rtgs | Model, status, audit, parser/penulis subset MT103 |
| Repository | Penyimpanan JSON per transaksi |
| TransferFile | Simulasi download/upload melalui folder lokal |
| Checks (di tests) | Pemeriksaan alur dan background worker tanpa test package |

Dependensi inti: Gateway -> Adapter/Microservice -> Service -> Repository -> Library.Rtgs -> Library.
Adapter juga memakai TransferFile. Tidak ada referensi ke DLL atau project lama.

## Mencoba alur file otomatis

Dari root solution setelah aplikasi berjalan:

```powershell
New-Item -ItemType Directory -Force data/remote-inbox
Copy-Item samples/MT103-VA.txt,samples/MT103-NONVA.txt data/remote-inbox/
Invoke-RestMethod http://localhost:5080/api/rtgs/payments
```

Worker polling setiap dua detik. Tunggu sekitar delapan detik untuk VA mencapai Settle.
File sumber dipindah ke completed. File MT103 yang tidak valid masuk failed.
Reference yang pernah diimport tidak diproses ulang; kirim dengan reference baru untuk
transaksi baru. File duplicate diarsipkan, dan warning ditulis ke log.

| Folder dalam data | Isi |
| --- | --- |
| remote-inbox | Simulasi folder server FTP sumber; mendukung subfolder |
| incoming | File setelah download lokal |
| completed | Arsip input berhasil diimport, termasuk duplicate yang dilewati |
| failed | Input MT103 yang tidak valid |
| cbs-outbox | File hasil untuk CBS sebelum transfer |
| remote-outbox | Simulasi tujuan FTP/CBS setelah transfer |
| transactions | JSON transaksi dan riwayat audit yang bertahan setelah restart |

Pada contoh VA, Rp1.000.000 menjadi Rp997.500 dengan fee Rp2.500, dan rekening
tujuan diganti menjadi rekening induk 1000000001. Untuk non-VA, isi file tetap sama.

## Alur Victoria yang diikuti

1. STPG membaca MT103 -> Unposting; klasifikasi VA berdasarkan panjang rekening (default 16-18).
2. VA: ValidatePayment -> Posting -> buat MT103 rekening induk/nominal bersih -> Posted.
3. Posted -> MutasiByRef -> SetToPostFee -> VM40 -> SetToSettle.
   Cabang MutasiByRef tanpa data juga lanjut VM40 sesuai kode sumber.
4. PayOffUs berhasil -> Settle. Respons tidak pasti -> InquiryStatus.
   Inquiry sukses -> Settle; masih pending -> RetryToSettle -> SettleHold setelah batas retry.
5. VM40 gagal setelah batas retry -> HoldPostFee.
6. Validasi VA gagal setelah batas retry -> FailedToValidate dan teruskan file asli.
7. Non-VA -> PostingNonVa dan teruskan file asli; pemrosesan CBS selanjutnya di luar demo.

Setiap percobaan dan transisi direkam di audit. Satu pemanggilan process menjalankan satu
langkah per transaksi; beberapa transisi terkait MutasiByRef/VM40 berada pada langkah yang sama.
MaxAttempts berlaku per tahap. Nilai uang memakai decimal.

## API dan skenario simulator

| Method | Path | Fungsi |
| --- | --- | --- |
| GET | /health | Status host |
| GET | /api/system/settings | Konfigurasi demo |
| GET | /api/rtgs/payments | Semua transaksi beserta audit |
| GET | /api/rtgs/payments/{reference} | Detail; 404 jika tidak ada |
| POST | /api/rtgs/payments | Import JSON Content dan Scenario; 201, 400, atau 409 |
| POST | /api/rtgs/process | Jalankan satu langkah untuk seluruh transaksi |

```powershell
$body = @{
    content = (Get-Content samples/MT103-VA.txt -Raw).Replace('DEMO-VA-001','TIMEOUT-001')
    scenario = 'OffUsTimeout'
} | ConvertTo-Json
Invoke-RestMethod http://localhost:5080/api/rtgs/payments -Method Post -ContentType application/json -Body $body
```

Scenario: Success, ValidationFailure, MutasiMissing, Vm40Failure, OffUsTimeout, InquiryPending.
Scenario API tersimpan per transaksi; file otomatis menggunakan Success.
Set WorkerEnabled=false jika ingin mengamati setiap tahap melalui POST /api/rtgs/process.
API import langsung masuk repository; file keluaran ditransfer oleh worker jika aktif.

## Verifikasi

```powershell
./scripts/check.ps1
```

Script membangun seluruh solution, menjalankan pemeriksaan alur C#, dan smoke test HTTP
(membutuhkan Python 3). Test memakai folder temporary dan instance HTTP tersendiri.

## Pemetaan ke kode asal

Di PaymentHub-Victoria-Adapter-Rtgs:

- Cbs/PaymentHub.Common.Adapter.Rtgs.Cbs/Worker/Victoria/RunVictoriaTask.cs:
  orkestrasi Victoria; ClaimToken, ValidatePayment, MutasiByRef, PayOffUs.
- Stpg/PaymentHub.Common.Adapter.Rtgs.Stpg/Worker/Victoria/RunVictoriaTask.cs dan
  Stpg/Service/PaymentHub.Common.Adapter.Rtgs.Stpg.Service/Bank/Victoria/PaymentInService.cs:
  polling incoming dan import transaksi.
- Cbs/Service/PaymentHub.Common.Adapter.Rtgs.Cbs.Service/Bank/Victoria/:
  ValidatePaymentService.cs, PaymentValidationVictoriaMutasiByRefService.cs,
  PaymentValidationVictoriaVM40Service.cs, PaymentValidationVictoriaPayOffUsService.cs.
- PaymentInService.cs di folder CBS Victoria: pembuatan MT103 baru dan penerusan file asli.

Peran gateway mengacu PaymentHub-Victoria-Gateway; peran transfer mengacu
PaymentHub-Victoria-Transfer-File (download/upload dan polling). Library dan
microservice diringkas sesuai kebutuhan adapter, bukan disalin seluruh fiturnya.

## Batas penyederhanaan

Ini implementasi baru untuk belajar dan pengembangan lokal. Gateway menggabungkan route
module di satu host; tidak memakai reverse proxy Ocelot atau jaringan antarservice.
Token PaymentHub/ClaimToken bank, HTTP CBS/VA, HMAC, FTP/FTPS, dan database produksi
tidak diimplementasikan; simulator bank dipanggil melalui IVictoriaBank.
BOD/EOD, outgoing RTGS, approval, UI, report, dan bank selain Victoria tidak disertakan.

Parser hanya mendukung subset MT103 :20:, :32A: IDR, dan :59:; tahun 2000-2099.
Header/trailer SWIFT tidak divalidasi penuh. Nama dan field lain dipertahankan saat rewrite.
API demo tidak memakai autentikasi dan binding default hanya localhost.

Repository memakai penulisan temporary file lalu rename dan lock untuk satu proses.
Ini bukan transaksi atomik lintas repository, transfer file, dan CBS. Setelah crash, output
yang belum diunggah bisa ditulis ulang; transfer hanya menerima duplicate dengan isi sama.
Simulator tidak membuktikan exactly-once posting di bank. Untuk integrasi bank nyata,
implementasikan kontrak HTTP, idempotency, rekonsiliasi, database, dan recovery sesuai spesifikasinya.
