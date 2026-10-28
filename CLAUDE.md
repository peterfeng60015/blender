# CLAUDE.md

## 語言

- 所有回應一律使用繁體中文。程式碼、指令、檔名、識別字保持原樣，不翻譯。

## Git

- Commit message 開頭一律加類型前綴，後面接繁體中文描述，例如 `feat: 新增角色移動`。
  - `feat`：新功能
  - `fix`：修正錯誤
  - `docs`：文件
  - `refactor`：重構，不改變行為
  - `chore`：設定、.gitignore、套件等雜項
- push 前先檢查要推的 commit 裡有沒有單檔超過 10MB 的檔案（已由 Git LFS 追蹤的除外）。有的話先列出檔名與大小告訴使用者，問要改用 LFS、排除，還是照推，等同意後才 push。
- 目前由 LFS 追蹤的檔案見 `.gitattributes`。
