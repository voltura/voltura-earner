; Written Cantonese (Hong Kong, 3076). Only the pages used by this installer
; are enabled; NSIS /WX catches missing strings if more pages are added.
!insertmacro LANGFILE "Cantonese" "Cantonese" "粵語" "Cantonese"

!ifdef MUI_WELCOMEPAGE
  ${LangFileString} MUI_TEXT_WELCOME_INFO_TITLE "歡迎使用 $(^NameDA) 安裝程式"
  ${LangFileString} MUI_TEXT_WELCOME_INFO_TEXT "呢個程式會帶你完成 $(^NameDA) 嘅安裝。$\r$\n$\r$\n建議你開始安裝之前先關閉其他應用程式，咁就可以更新相關嘅系統檔案，而唔使重新啟動電腦。$\r$\n$\r$\n$_CLICK"
!endif

!ifdef MUI_LICENSEPAGE
  ${LangFileString} MUI_TEXT_LICENSE_TITLE "授權合約"
  ${LangFileString} MUI_TEXT_LICENSE_SUBTITLE "安裝 $(^NameDA) 之前，請先睇清楚授權條款。"
  ${LangFileString} MUI_INNERTEXT_LICENSE_BOTTOM "如果你接受合約嘅條款，請撳「我同意」繼續。你必須接受合約先可以安裝 $(^NameDA)。"
  ${LangFileString} MUI_INNERTEXT_LICENSE_BOTTOM_CHECKBOX "如果你接受合約嘅條款，請勾選下面嘅方格。你必須接受合約先可以安裝 $(^NameDA)。$_CLICK"
  ${LangFileString} MUI_INNERTEXT_LICENSE_BOTTOM_RADIOBUTTONS "如果你接受合約嘅條款，請揀下面第一個選項。你必須接受合約先可以安裝 $(^NameDA)。$_CLICK"
  ${LangFileString} MUI_INNERTEXT_LICENSE_TOP "撳 Page Down 睇合約其餘嘅內容。"
!endif

!ifdef MUI_INSTFILESPAGE
  ${LangFileString} MUI_TEXT_INSTALLING_TITLE "安裝緊"
  ${LangFileString} MUI_TEXT_INSTALLING_SUBTITLE "正在安裝 $(^NameDA)，請等一陣。"
  ${LangFileString} MUI_TEXT_FINISH_TITLE "安裝完成"
  ${LangFileString} MUI_TEXT_FINISH_SUBTITLE "已成功完成安裝。"
  ${LangFileString} MUI_TEXT_ABORT_TITLE "安裝已中止"
  ${LangFileString} MUI_TEXT_ABORT_SUBTITLE "安裝未能完成。"
!endif

!ifdef MUI_UNINSTFILESPAGE
  ${LangFileString} MUI_UNTEXT_UNINSTALLING_TITLE "解除安裝緊"
  ${LangFileString} MUI_UNTEXT_UNINSTALLING_SUBTITLE "正在移除 $(^NameDA)，請等一陣。"
  ${LangFileString} MUI_UNTEXT_FINISH_TITLE "解除安裝完成"
  ${LangFileString} MUI_UNTEXT_FINISH_SUBTITLE "已成功完成解除安裝。"
  ${LangFileString} MUI_UNTEXT_ABORT_TITLE "解除安裝已中止"
  ${LangFileString} MUI_UNTEXT_ABORT_SUBTITLE "解除安裝未能完成。"
!endif

!ifdef MUI_FINISHPAGE
  ${LangFileString} MUI_TEXT_FINISH_INFO_TITLE "$(^NameDA) 安裝完成"
  ${LangFileString} MUI_TEXT_FINISH_INFO_TEXT "$(^NameDA) 已經裝好喺你部電腦。$\r$\n$\r$\n撳「完成」關閉安裝程式。"
  ${LangFileString} MUI_TEXT_FINISH_INFO_REBOOT "你需要重新啟動電腦先可以完成 $(^NameDA) 嘅安裝。要唔要而家重新啟動？"
  ${LangFileString} MUI_TEXT_FINISH_REBOOTNOW "而家重新啟動"
  ${LangFileString} MUI_TEXT_FINISH_REBOOTLATER "我想遲啲自行重新啟動"
  ${LangFileString} MUI_TEXT_FINISH_RUN "開啟 $(^NameDA)(&R)"
  ${LangFileString} MUI_TEXT_FINISH_SHOWREADME "顯示說明文件(&S)"
  ${LangFileString} MUI_BUTTONTEXT_FINISH "完成(&F)"
!endif

!ifdef MUI_UNCONFIRMPAGE
  ${LangFileString} MUI_UNTEXT_CONFIRM_TITLE "解除安裝 $(^NameDA)"
  ${LangFileString} MUI_UNTEXT_CONFIRM_SUBTITLE "由你部電腦移除 $(^NameDA)。"
!endif

!ifdef MUI_ABORTWARNING
  ${LangFileString} MUI_TEXT_ABORTWARNING "你確定要退出 $(^Name) 安裝程式？"
!endif
