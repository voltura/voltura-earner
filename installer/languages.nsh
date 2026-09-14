; Keep IDs and native labels aligned with the application LanguageCatalog.
; NSIS supplies the standard wizard translations; Cantonese is maintained here.
!macro VolturaLanguage Id Nlf NativeName
  !define LANGFILE_LANGDLL_FMT "${NativeName}"
  !if "${Id}" == "yue"
    !insertmacro MUI_LANGUAGEEX "languages" "${Nlf}"
  !else
    !insertmacro MUI_LANGUAGE "${Nlf}"
  !endif
  !undef LANGFILE_LANGDLL_FMT
!macroend

!insertmacro VolturaLanguage "en" "English" "English"
!insertmacro VolturaLanguage "sv" "Swedish" "Svenska"
!insertmacro VolturaLanguage "de" "German" "Deutsch"
!insertmacro VolturaLanguage "fr" "French" "Français"
!insertmacro VolturaLanguage "da" "Danish" "Dansk"
!insertmacro VolturaLanguage "fi" "Finnish" "Suomi"
!insertmacro VolturaLanguage "is" "Icelandic" "Íslenska"
!insertmacro VolturaLanguage "nb" "Norwegian" "Norsk bokmål"
!insertmacro VolturaLanguage "pl" "Polish" "Polski"
!insertmacro VolturaLanguage "it" "Italian" "Italiano"
!insertmacro VolturaLanguage "es" "Spanish" "Español"
!insertmacro VolturaLanguage "yue" "Cantonese" "粵語"
!insertmacro VolturaLanguage "ja" "Japanese" "日本語"
!insertmacro VolturaLanguage "pt-BR" "PortugueseBR" "Português (Brasil)"
!insertmacro VolturaLanguage "zh-Hans" "SimpChinese" "简体中文"
!insertmacro VolturaLanguage "zh-Hant" "TradChinese" "繁體中文"
!insertmacro VolturaLanguage "nl" "Dutch" "Nederlands"
!insertmacro VolturaLanguage "ko" "Korean" "한국어"
!insertmacro VolturaLanguage "ru" "Russian" "Русский"
!insertmacro VolturaLanguage "tr" "Turkish" "Türkçe"
!insertmacro VolturaLanguage "id" "Indonesian" "Bahasa Indonesia"

; Application-owned setup and uninstall messages. Native Windows dialogs and
; the original license text retain their own language.
!macro VolturaSetupStrings Nlf Failure Runtime Removal Architecture Running
  LangString Failed ${LANG_${Nlf}} "${Failure}"
  LangString RuntimeFailed ${LANG_${Nlf}} "${Runtime}"
  LangString RemoveData ${LANG_${Nlf}} "${Removal}"
  LangString RequiresX64 ${LANG_${Nlf}} "${Architecture}"
  LangString AppRunning ${LANG_${Nlf}} "${Running}"
!macroend

!insertmacro VolturaSetupStrings English \
  "Setup could not complete. The previous installation was restored where possible. See the installation details and retry." \
  "The .NET 10 Desktop Runtime could not be installed. Retry, or use the offline installer." \
  "Also permanently delete work history, reports, settings, logs, and downloaded updates in the default data folder? Files outside this folder are kept." \
  "Windows x64 is required." \
  "Exit Voltura Earner before retrying setup or uninstall."
!insertmacro VolturaSetupStrings Swedish \
  "Installationen kunde inte slutföras. Den tidigare installationen återställdes om möjligt. Se installationsinformationen och försök igen." \
  ".NET 10 Desktop Runtime kunde inte installeras. Försök igen eller använd offline-installationsprogrammet." \
  "Vill du också permanent ta bort arbetshistorik, rapporter, inställningar, loggar och hämtade uppdateringar i standardmappen för data? Filer utanför den här mappen behålls." \
  "Windows x64 krävs." \
  "Avsluta Voltura Earner innan du försöker installera eller avinstallera igen."
!insertmacro VolturaSetupStrings German \
  "Setup konnte nicht abgeschlossen werden. Die vorherige Installation wurde nach Möglichkeit wiederhergestellt. Details prüfen und erneut versuchen." \
  ".NET 10 Desktop Runtime konnte nicht installiert werden. Erneut versuchen oder Offline-Installer verwenden." \
  "Auch Arbeitsverlauf, Berichte, Einstellungen, Protokolle und heruntergeladene Updates im Standarddatenordner dauerhaft löschen? Dateien außerhalb dieses Ordners bleiben erhalten." \
  "Windows x64 ist erforderlich." \
  "Beenden Sie Voltura Earner, bevor Sie die Installation oder Deinstallation erneut versuchen."
!insertmacro VolturaSetupStrings French \
  "L’installation n’a pas pu être terminée. L’installation précédente a été restaurée dans la mesure du possible. Consultez les détails de l’installation et réessayez." \
  "Le .NET 10 Desktop Runtime n’a pas pu être installé. Réessayez ou utilisez le programme d’installation hors ligne." \
  "Supprimer aussi définitivement l’historique de travail, les rapports, les paramètres, les journaux et les mises à jour téléchargées du dossier de données par défaut ? Les fichiers situés en dehors de ce dossier sont conservés." \
  "Windows x64 est requis." \
  "Quittez Voltura Earner avant de réessayer l’installation ou la désinstallation."
!insertmacro VolturaSetupStrings Danish \
  "Installationen kunne ikke fuldføres. Den tidligere installation blev gendannet, hvor det var muligt. Se installationsoplysningerne, og prøv igen." \
  ".NET 10 Desktop Runtime kunne ikke installeres. Prøv igen, eller brug offlineinstallationsprogrammet." \
  "Vil du også slette arbejdshistorik, rapporter, indstillinger, logfiler og hentede opdateringer i standarddatamappen permanent? Filer uden for denne mappe bevares." \
  "Windows x64 er påkrævet." \
  "Afslut Voltura Earner, før du prøver at installere eller afinstallere igen."
!insertmacro VolturaSetupStrings Finnish \
  "Asennusta ei voitu viimeistellä. Aiempi asennus palautettiin mahdollisuuksien mukaan. Tarkista asennuksen tiedot ja yritä uudelleen." \
  ".NET 10 Desktop Runtime -ympäristöä ei voitu asentaa. Yritä uudelleen tai käytä offline-asennusohjelmaa." \
  "Poistetaanko myös työhistoria, raportit, asetukset, lokit ja ladatut päivitykset pysyvästi oletusdatakansiosta? Tämän kansion ulkopuoliset tiedostot säilytetään." \
  "Windows x64 vaaditaan." \
  "Sulje Voltura Earner ennen asennuksen tai poiston yrittämistä uudelleen."
!insertmacro VolturaSetupStrings Icelandic \
  "Ekki tókst að ljúka uppsetningunni. Fyrri uppsetning var endurheimt þar sem hægt var. Skoðaðu upplýsingar um uppsetninguna og reyndu aftur." \
  "Ekki tókst að setja upp .NET 10 Desktop Runtime. Reyndu aftur eða notaðu uppsetningarforritið fyrir ótengda uppsetningu." \
  "Viltu einnig eyða vinnusögu, skýrslum, stillingum, annálum og sóttum uppfærslum í sjálfgefnu gagnamöppunni varanlega? Skrár utan þessarar möppu haldast." \
  "Windows x64 er nauðsynlegt." \
  "Lokaðu Voltura Earner áður en þú reynir uppsetningu eða fjarlægingu aftur."
!insertmacro VolturaSetupStrings Norwegian \
  "Installasjonen kunne ikke fullføres. Den forrige installasjonen ble gjenopprettet der det var mulig. Se installasjonsdetaljene og prøv igjen." \
  ".NET 10 Desktop Runtime kunne ikke installeres. Prøv igjen, eller bruk det frakoblede installasjonsprogrammet." \
  "Vil du også slette arbeidshistorikk, rapporter, innstillinger, logger og nedlastede oppdateringer i standarddatamappen permanent? Filer utenfor denne mappen beholdes." \
  "Windows x64 kreves." \
  "Avslutt Voltura Earner før du prøver å installere eller avinstallere på nytt."
!insertmacro VolturaSetupStrings Polish \
  "Nie udało się ukończyć instalacji. Poprzednia instalacja została przywrócona tam, gdzie było to możliwe. Sprawdź szczegóły instalacji i spróbuj ponownie." \
  "Nie udało się zainstalować środowiska .NET 10 Desktop Runtime. Spróbuj ponownie lub użyj instalatora offline." \
  "Czy również trwale usunąć historię pracy, raporty, ustawienia, dzienniki i pobrane aktualizacje z domyślnego folderu danych? Pliki poza tym folderem zostaną zachowane." \
  "Wymagany jest system Windows x64." \
  "Zamknij Voltura Earner przed ponowną próbą instalacji lub odinstalowania."
!insertmacro VolturaSetupStrings Italian \
  "Impossibile completare l’installazione. L’installazione precedente è stata ripristinata ove possibile. Consulta i dettagli dell’installazione e riprova." \
  "Impossibile installare .NET 10 Desktop Runtime. Riprova o usa il programma di installazione offline." \
  "Eliminare definitivamente anche cronologia di lavoro, rapporti, impostazioni, registri e aggiornamenti scaricati nella cartella dati predefinita? I file al di fuori di questa cartella verranno conservati." \
  "È richiesto Windows x64." \
  "Chiudi Voltura Earner prima di riprovare l’installazione o la disinstallazione."
!insertmacro VolturaSetupStrings Spanish \
  "No se pudo completar la instalación. Se restauró la instalación anterior cuando fue posible. Consulta los detalles de la instalación e inténtalo de nuevo." \
  "No se pudo instalar .NET 10 Desktop Runtime. Inténtalo de nuevo o utiliza el instalador sin conexión." \
  "¿Eliminar también permanentemente el historial de trabajo, los informes, la configuración, los registros y las actualizaciones descargadas de la carpeta de datos predeterminada? Se conservarán los archivos fuera de esta carpeta." \
  "Se requiere Windows x64." \
  "Cierra Voltura Earner antes de volver a intentar la instalación o desinstalación."
!insertmacro VolturaSetupStrings Cantonese \
  "安裝未能完成。已盡可能還原之前嘅安裝。請睇吓安裝詳情，再試一次。" \
  "裝唔到 .NET 10 Desktop Runtime。請再試一次，或者用離線安裝程式。" \
  "要唔要同時永久刪除預設資料夾入面嘅工作記錄、報告、設定、日誌同已下載嘅更新？呢個資料夾以外嘅檔案會保留。" \
  "需要 Windows x64。" \
  "請先結束 Voltura Earner，再重試安裝或解除安裝。"
!insertmacro VolturaSetupStrings Japanese \
  "インストールを完了できませんでした。可能な範囲で以前のインストールを復元しました。インストールの詳細を確認して、もう一度お試しください。" \
  ".NET 10 Desktop Runtime をインストールできませんでした。もう一度試すか、オフラインインストーラーを使用してください。" \
  "既定のデータフォルダーにある作業履歴、レポート、設定、ログ、ダウンロード済みの更新も完全に削除しますか？このフォルダーの外にあるファイルは保持されます。" \
  "Windows x64 が必要です。" \
  "Voltura Earner を終了してから、インストールまたはアンインストールを再試行してください。"
!insertmacro VolturaSetupStrings PortugueseBR \
  "Não foi possível concluir a instalação. A instalação anterior foi restaurada quando possível. Confira os detalhes da instalação e tente novamente." \
  "Não foi possível instalar o .NET 10 Desktop Runtime. Tente novamente ou use o instalador offline." \
  "Excluir também permanentemente o histórico de trabalho, relatórios, configurações, registros e atualizações baixadas da pasta de dados padrão? Os arquivos fora desta pasta serão mantidos." \
  "É necessário o Windows x64." \
  "Encerre o Voltura Earner antes de tentar instalar ou desinstalar novamente."
!insertmacro VolturaSetupStrings SimpChinese \
  "无法完成安装。已尽可能恢复先前的安装。请查看安装详情并重试。" \
  "无法安装 .NET 10 Desktop Runtime。请重试，或使用离线安装程序。" \
  "是否同时永久删除默认数据文件夹中的工作历史、报表、设置、日志和已下载的更新？此文件夹以外的文件将保留。" \
  "需要 Windows x64。" \
  "请先退出 Voltura Earner，再重试安装或卸载。"
!insertmacro VolturaSetupStrings TradChinese \
  "無法完成安裝。已盡可能還原先前的安裝。請查看安裝詳細資料並重試。" \
  "無法安裝 .NET 10 Desktop Runtime。請重試，或使用離線安裝程式。" \
  "是否同時永久刪除預設資料夾中的工作歷程、報表、設定、日誌和已下載的更新？此資料夾以外的檔案將保留。" \
  "需要 Windows x64。" \
  "請先結束 Voltura Earner，再重試安裝或解除安裝。"
!insertmacro VolturaSetupStrings Dutch \
  "De installatie kon niet worden voltooid. De vorige installatie is waar mogelijk hersteld. Bekijk de installatiedetails en probeer het opnieuw." \
  ".NET 10 Desktop Runtime kon niet worden geïnstalleerd. Probeer het opnieuw of gebruik het offline-installatieprogramma." \
  "Ook de werkgeschiedenis, rapporten, instellingen, logboeken en gedownloade updates in de standaardgegevensmap permanent verwijderen? Bestanden buiten deze map blijven behouden." \
  "Windows x64 is vereist." \
  "Sluit Voltura Earner af voordat u de installatie of verwijdering opnieuw probeert."
!insertmacro VolturaSetupStrings Korean \
  "설치를 완료하지 못했습니다. 가능한 경우 이전 설치를 복원했습니다. 설치 세부 정보를 확인하고 다시 시도하세요." \
  ".NET 10 Desktop Runtime을 설치하지 못했습니다. 다시 시도하거나 오프라인 설치 프로그램을 사용하세요." \
  "기본 데이터 폴더의 작업 기록, 보고서, 설정, 로그 및 다운로드한 업데이트도 영구적으로 삭제하시겠습니까? 이 폴더 외부의 파일은 유지됩니다." \
  "Windows x64가 필요합니다." \
  "Voltura Earner를 종료한 후 설치 또는 제거를 다시 시도하세요."
!insertmacro VolturaSetupStrings Russian \
  "Не удалось завершить установку. Предыдущая установка по возможности восстановлена. Просмотрите подробности установки и повторите попытку." \
  "Не удалось установить .NET 10 Desktop Runtime. Повторите попытку или используйте автономный установщик." \
  "Также безвозвратно удалить историю работы, отчёты, настройки, журналы и загруженные обновления из папки данных по умолчанию? Файлы вне этой папки сохранятся." \
  "Требуется Windows x64." \
  "Выйдите из Voltura Earner перед повторной установкой или удалением."
!insertmacro VolturaSetupStrings Turkish \
  "Kurulum tamamlanamadı. Önceki kurulum mümkün olduğunca geri yüklendi. Kurulum ayrıntılarını inceleyip yeniden deneyin." \
  ".NET 10 Desktop Runtime yüklenemedi. Yeniden deneyin veya çevrimdışı yükleyiciyi kullanın." \
  "Varsayılan veri klasöründeki çalışma geçmişi, raporlar, ayarlar, günlükler ve indirilen güncellemeler de kalıcı olarak silinsin mi? Bu klasörün dışındaki dosyalar korunur." \
  "Windows x64 gereklidir." \
  "Kurulumu veya kaldırmayı yeniden denemeden önce Voltura Earner’dan çıkın."
!insertmacro VolturaSetupStrings Indonesian \
  "Instalasi tidak dapat diselesaikan. Instalasi sebelumnya dipulihkan jika memungkinkan. Lihat detail instalasi dan coba lagi." \
  ".NET 10 Desktop Runtime tidak dapat diinstal. Coba lagi atau gunakan penginstal offline." \
  "Hapus juga secara permanen riwayat kerja, laporan, pengaturan, log, dan pembaruan yang diunduh dalam folder data bawaan? File di luar folder ini tetap disimpan." \
  "Memerlukan Windows x64." \
  "Keluar dari Voltura Earner sebelum mencoba menginstal atau menghapus instalasi lagi."
