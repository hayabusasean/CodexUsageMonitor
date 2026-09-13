# 隱私、來源與限制

[首頁](../README.zh-TW.md) · [English](privacy-and-limits.en.md)

## 本機額度

程式透過本機官方Codex app-server的帳號／額度介面取得資料。監控工具不要求API Key，
不讀瀏覽器Cookie或直接讀auth.json；官方Codex程序仍會使用自己的登入與網路連線。
工具不為監看而啟動模型回合、兌換券或購買額度，也不讀專案原始碼／對話作監測。
資料保存在 `%LOCALAPPDATA%\CodexUsageMonitor`，不是EXE旁邊；可攜程式不等於免本機狀態。

本工具沒有使用者遙測。公告來源仍會收到一般HTTPS請求和網路資訊；手動開啟原文會交給
你的瀏覽器處理，瀏覽器自己的登入與隱私設定仍然適用。

## 監測的是哪些地方

這是程式設定的來源，不是目前每個站都可讀的保證；發布時須與實際source一致。

| 來源 | 角色 | 設定網址 |
| --- | --- | --- |
| Codex Changelog | OpenAI官方 | https://developers.openai.com/codex/changelog/rss.xml |
| OpenAI Release Notes | OpenAI官方 | https://openai.com/products/release-notes/ |
| OpenAI Status | 官方狀態；僅作輔助背景 | https://status.openai.com/feed.rss |
| OpenAI Help Center | OpenAI官方 | https://help.openai.com/en/articles/20001498-how-banked-codex-resets-work |
| ModelYard team signal relay | 團隊貼文的第三方轉送來源 | https://tibo.modelyard.dev/feed.xml |
| Codex Reset team signal relay | 團隊貼文的第三方轉送來源 | https://codex-reset.com/tibo |

不自動登入或爬私人FB社團、X登入頁。團隊貼文可由社群站轉送，原文與轉送站連結分開。
同一篇貼文被兩站轉送，不等於兩個獨立官方確認。

照片當時是4/6可讀：Changelog、Status與兩個團隊轉送來源；Release Notes和Help回403。
這是當時狀態，不是持續服務保證。403也不是要使用者交Cookie或關防護去繞過。

## 提醒是提醒，觀察是觀察

黃色WATCH表示有可追溯、值得留意的訊號；紅色INCOMING要求更明確的訊息，
不是把第三方網站的87%預測當本工具結論。兩者都不保證你的帳號何時補滿。
Status事故只作輔助背景；發一張重置券，不等於額度已自動重置。

來源可讀率採目前啟用來源的讀取／解析／新鮮度規則。它不代表全網涵蓋率、
訊息完整率、獨立證據比例或重置機率。站抓得到，也可能還沒轉來最新一篇。

金色事件代表本機觀察到補額及可取得的週期資料變化。觀察時間不是伺服器精確執行時間；
原因沒有充分證據就保留未知。讓已滿足的舊提醒退場，不代表把原貼文改成全域已確認。

## 分享與相容性

CSV與詳細分析Log可能包含使用時間、模式或帳號背景，分享給AI或GitHub前請檢查。
不要上傳密碼、Token、auth.json、QR、救援碼、公司資料或未審閱私人Log。
回報bug先提供程式版本、Windows版本、發生情況與可選的安全截圖即可。

程式以Windows x64為目標。先前rc.3曾在Win11使用一整天；最新Gold有其工程檢查及作者接受，
不代表每個Win11／DPI／多螢幕／全螢幕情況都測過。這個案例也不是完整72小時持續測試。
EXE尚未簽章，checksum只核對檔案身分，不是安全認證。

這是私人開源作品，不承諾即時支援或SLA。重要匯出請自行保存，回報問題勿夾帶敏感資料。
