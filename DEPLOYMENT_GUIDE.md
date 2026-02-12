# 🚀 Deployment Guide - CareerIntel

## Quick Deployment Options

### 🌟 Recommended: DigitalOcean App Platform ($5/month)

**Pros**: Easy, cheap, automatic SSL, automatic restarts
**Time**: 10 minutes

#### Step-by-Step:

1. **Push code to GitHub** (if not already):
   ```bash
   git add .
   git commit -m "Ready for deployment"
   git push origin master
   ```

2. **Create DigitalOcean account**:
   - Go to https://www.digitalocean.com/
   - Sign up (get $200 free credit with promo)

3. **Deploy from GitHub**:
   - Click "Create" → "Apps"
   - Connect GitHub repository
   - Select `dotnet-career-intel` repository
   - DigitalOcean auto-detects .NET app
   - Click "Next" → "Next" → "Launch App"
   - Wait 5-10 minutes for build

4. **Result**: Live app at `https://your-app-name.ondigitalocean.app`

**Cost**: $5/month (512 MB RAM, enough for this app)

---

### 🐳 Docker + Any Cloud Platform

Your app already has a Dockerfile! You can deploy to:
- **Railway.app** (Free tier, then $5/month)
- **Render.com** (Free tier, then $7/month)
- **Fly.io** ($0-5/month)

#### Option A: Railway.app (Easiest)

1. Go to https://railway.app/
2. Sign up with GitHub
3. Click "New Project" → "Deploy from GitHub repo"
4. Select `dotnet-career-intel`
5. Railway auto-builds with Dockerfile
6. Wait 3-5 minutes
7. **Result**: Live at `https://your-app.up.railway.app`

**Cost**: Free tier (500 hours/month), then $5/month

#### Option B: Render.com

1. Go to https://render.com/
2. Sign up with GitHub
3. Click "New +" → "Web Service"
4. Connect `dotnet-career-intel` repo
5. Render detects Dockerfile
6. Set environment: `Docker`
7. Click "Create Web Service"

**Cost**: Free tier (limited), then $7/month

---

### ☁️ Azure App Service (Free Tier Available)

**Pros**: Microsoft's cloud, best for .NET, free tier available
**Time**: 15 minutes

#### Steps:

1. **Install Azure CLI**:
   ```powershell
   winget install Microsoft.AzureCLI
   ```

2. **Login**:
   ```bash
   az login
   ```

3. **Create resource group**:
   ```bash
   az group create --name CareerIntelRG --location eastus
   ```

4. **Create App Service**:
   ```bash
   az webapp create --resource-group CareerIntelRG --plan CareerIntelPlan --name careerintel-yourname --runtime "DOTNET|10.0"
   ```

5. **Deploy**:
   ```bash
   cd src/CareerIntel.Web
   dotnet publish -c Release
   az webapp deployment source config-zip --resource-group CareerIntelRG --name careerintel-yourname --src publish.zip
   ```

**Cost**: Free tier (F1) or $13/month (B1 - better performance)

---

### 🐧 Self-Hosted VPS (Full Control)

**Providers**: DigitalOcean Droplet, Linode, Vultr, Hetzner
**Cost**: $4-6/month

#### Ubuntu Server Setup:

1. **Create VPS** (Ubuntu 24.04):
   - 1 GB RAM, 1 CPU core - $4-6/month
   - Get IP address (e.g., 142.93.123.45)

2. **SSH into server**:
   ```bash
   ssh root@142.93.123.45
   ```

3. **Install .NET 10**:
   ```bash
   wget https://dot.net/v1/dotnet-install.sh
   chmod +x dotnet-install.sh
   ./dotnet-install.sh --channel 10.0
   ```

4. **Upload your app**:
   ```bash
   # On your local machine
   dotnet publish -c Release -o publish
   scp -r publish root@142.93.123.45:/var/www/careerintel
   ```

5. **Install Nginx reverse proxy**:
   ```bash
   apt update
   apt install nginx
   ```

6. **Configure Nginx**:
   ```bash
   nano /etc/nginx/sites-available/careerintel
   ```

   Paste:
   ```nginx
   server {
       listen 80;
       server_name 142.93.123.45;
       location / {
           proxy_pass http://localhost:5050;
           proxy_http_version 1.1;
           proxy_set_header Upgrade $http_upgrade;
           proxy_set_header Connection keep-alive;
           proxy_set_header Host $host;
           proxy_cache_bypass $http_upgrade;
       }
   }
   ```

   Enable:
   ```bash
   ln -s /etc/nginx/sites-available/careerintel /etc/nginx/sites-enabled/
   systemctl restart nginx
   ```

7. **Run app as systemd service**:
   ```bash
   nano /etc/systemd/system/careerintel.service
   ```

   Paste:
   ```ini
   [Unit]
   Description=CareerIntel Blazor App
   After=network.target

   [Service]
   WorkingDirectory=/var/www/careerintel
   ExecStart=/root/.dotnet/dotnet CareerIntel.Web.dll
   Restart=always
   RestartSec=10
   SyslogIdentifier=careerintel
   User=www-data
   Environment=ASPNETCORE_ENVIRONMENT=Production
   Environment=DOTNET_ROOT=/root/.dotnet

   [Install]
   WantedBy=multi-user.target
   ```

   Start:
   ```bash
   systemctl enable careerintel
   systemctl start careerintel
   systemctl status careerintel
   ```

8. **Access**: http://142.93.123.45

9. **Optional - Add SSL with Let's Encrypt**:
   ```bash
   apt install certbot python3-certbot-nginx
   certbot --nginx -d yourdomain.com
   ```

---

## 📊 Comparison Table

| Platform | Cost | Setup Time | Pros | Cons |
|----------|------|------------|------|------|
| **Railway** | Free/$5 | 5 min | Easiest, auto-deploy | Limited free tier |
| **Render** | Free/$7 | 5 min | Free tier, auto-SSL | Slow cold starts (free) |
| **DigitalOcean App** | $5 | 10 min | Auto-deploy, fast | No free tier |
| **Azure Free** | Free | 15 min | Microsoft, .NET native | Complex setup |
| **Azure B1** | $13 | 15 min | Fast, reliable | More expensive |
| **VPS Self-hosted** | $4-6 | 30 min | Full control, cheap | Manual setup |
| **Fly.io** | $0-5 | 10 min | Good free tier | Docker required |

---

## 🎯 My Recommendation

**For you**: Start with **Railway.app** (5 minutes setup, free tier)

1. Push to GitHub
2. Connect Railway
3. Deploy automatically
4. **Done!** - Live in 5 minutes

Later, if you need more control or want to save money, migrate to **DigitalOcean VPS** ($4/month).

---

## 🔐 Environment Variables for Deployment

When deploying, set these (optional):

```bash
# Adzuna API (if you got the key)
ADZUNA_APP_ID=your_app_id
ADZUNA_APP_KEY=your_app_key

# Production settings
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://0.0.0.0:5050
```

---

## 📝 Before Deploying - Checklist

- [ ] Change default admin password in web UI
- [ ] Test locally: `dotnet run --project src/CareerIntel.Web`
- [ ] Commit all changes: `git add . && git commit -m "Ready for deployment"`
- [ ] Push to GitHub: `git push origin master`
- [ ] Choose deployment platform
- [ ] Deploy and verify it works
- [ ] Set up SSL certificate (if using VPS)

---

## 🚨 Important Notes

1. **Database**: SQLite file is stored in `data/` folder. On cloud platforms, this folder persists across restarts but may be lost on redeployments. For production, consider using PostgreSQL or Azure SQL Database.

2. **File persistence**: Cloud platforms may not persist files. If you need persistent storage:
   - Azure: Use Azure Blob Storage
   - DigitalOcean: Use Spaces
   - Railway/Render: Use external DB

3. **Memory**: 512 MB RAM is enough for ~500 active users. If you need more, upgrade to 1 GB plan.

4. **URL**: After deployment, your app will be at:
   - Railway: `https://your-app.up.railway.app`
   - Render: `https://your-app.onrender.com`
   - Azure: `https://careerintel-yourname.azurewebsites.net`
   - VPS: `http://your-ip-address` or `https://yourdomain.com`

---

## 🎉 Ready to Deploy?

Pick a platform and follow the steps above. If you need help, let me know which platform you chose!
