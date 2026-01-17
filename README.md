# Discord Blocked Account Detect Bot

Vibe Coding 練習 2

從 X 上讀取已封鎖用戶列表，並在有人於 Discord 上發送該用戶相關推文時新增表情提醒

### 本地開發

1. **複製 .env 文件模板**：
   ```bash
   cp .env.example .env
   ```

2. **編輯 .env 文件並填入您的配置**：
   ```bash
   DISCORD_TOKEN=your_discord_token
   XAPI_CLIENT_ID=your_x_client_id
   XAPI_CLIENT_SECRET=your_x_client_secret
   XAPI_REDIRECT_URI=http://127.0.0.1:3000/callback
   REDIS_CONNECTION_STRING=localhost:6379
   ```

3. **運行應用**：
   ```bash
   dotnet run
   ```

### Docker 部署

#### 使用 docker-compose（推薦）

1. **複製環境變數模板**：
   ```bash
   cp .env.example .env
   ```

2. **編輯 .env 文件**：
   ```bash
   DISCORD_TOKEN=your_discord_token
   XAPI_CLIENT_ID=your_x_client_id
   XAPI_CLIENT_SECRET=your_x_client_secret
   XAPI_REDIRECT_URI=http://your-domain:3000/callback
   REDIS_CONNECTION_STRING=redis:6379
   ```

3. **啟動容器**：
   ```bash
   docker-compose up -d
   ```

4. **查看日誌**：
   ```bash
   docker-compose logs -f bot
   ```

5. **停止容器**：
   ```bash
   docker-compose down
   ```

#### 手動 Docker 構建和運行

1. **構建鏡像**：
   ```bash
   docker build -t discord-blocked-account-bot .
   ```

2. **運行容器**：
   ```bash
   docker run -d \
     --name discord-bot \
     -e DISCORD_TOKEN=your_token \
     -e XAPI_CLIENT_ID=your_client_id \
     -e XAPI_CLIENT_SECRET=your_client_secret \
     -e REDIS_CONNECTION_STRING=redis:6379 \
     -v ./x_tokens.json:/app/x_tokens.json \
     discord-blocked-account-bot
   ```

### 環境變數說明

| 變數名稱 | 說明 | 預設值 |
|---------|------|--------|
| `DISCORD_TOKEN` | Discord 機器人令牌 | 必需 |
| `XAPI_CLIENT_ID` | X API 客戶端 ID | 必需 |
| `XAPI_CLIENT_SECRET` | X API 客戶端密鑰 | 必需 |
| `XAPI_REDIRECT_URI` | X OAuth 重定向 URI | `http://127.0.0.1:3000/callback` |
| `XAPI_SCOPES` | X API 作用域 | `tweet.read users.read block.read offline.access` |
| `REDIS_CONNECTION_STRING` | Redis 連接字符串 | `localhost:6379` |
| `ENV_FILE_PATH` | .env 文件路徑 | `.env` |