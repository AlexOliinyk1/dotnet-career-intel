# 🚂 Deploy to Railway in 5 Minutes

## Step 1: Push to GitHub (if not already)

```bash
git add .
git commit -m "Ready for Railway deployment"
git push origin master
```

## Step 2: Deploy to Railway

1. **Go to**: https://railway.app
2. **Sign in** with GitHub
3. Click **"New Project"**
4. Select **"Deploy from GitHub repo"**
5. Choose **`dotnet-career-intel`** repository
6. Railway will:
   - ✅ Auto-detect Dockerfile
   - ✅ Build the Docker image
   - ✅ Deploy automatically
   - ✅ Give you a public URL

## Step 3: Wait for Build (3-5 minutes)

Watch the build logs in Railway dashboard. You'll see:
```
Building Dockerfile...
✓ Stage 1: Restore dependencies
✓ Stage 2: Build
✓ Stage 3: Publish
✓ Stage 4: Runtime
Deployment successful!
```

## Step 4: Get Your URL

Railway will give you a URL like:
```
https://dotnet-career-intel-production.up.railway.app
```

Click it → Login with your admin password → **Done!**

## Step 5: (Optional) Add Custom Domain

In Railway dashboard:
1. Go to your app settings
2. Click "Domains"
3. Add your domain (e.g., `jobs.yourdomain.com`)
4. Update DNS records as shown
5. Railway auto-configures SSL!

## Cost

- **Free tier**: 500 execution hours/month, $5 credit
- **After free tier**: ~$5/month
- **Includes**: Auto-deploy on git push, free SSL, automatic restarts

## Environment Variables (Optional)

In Railway dashboard → "Variables" tab:

```
ADZUNA_APP_ID=your_app_id
ADZUNA_APP_KEY=your_app_key
```

## Troubleshooting

**Build fails?**
- Check Railway logs for errors
- Verify Dockerfile is in repo root
- Ensure all projects build locally: `dotnet build`

**App won't start?**
- Railway expects app on port from `$PORT` env var
- Your Dockerfile already handles this (port 5050)

**Database not persisting?**
- Railway provides persistent volumes
- Your SQLite file in `/app/data` will persist

## Auto-Deploy

Every time you push to GitHub:
```bash
git add .
git commit -m "Update feature"
git push
```

Railway automatically rebuilds and redeploys! 🎉

## ✅ That's It!

Your CareerIntel app is now live and accessible worldwide!
