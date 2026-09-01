# Run Dr. Care on a Mac

Simple instructions for running the application on your own MacBook.

## What these tools do

- **Colima** provides the small local computer that runs the backend services.
- **Docker** runs the database and API inside Colima.
- **The frontend** is the website you open in your browser.

You do not need to use AWS or SES for local testing. Documents are stored locally, and email can be sent through Microsoft Graph when you provide the development email details.

## Before you start

You need:

- A MacBook with an internet connection.
- The Dr. Care project downloaded on your Mac.
- Your Microsoft Graph email details if you want to test email sending.
- Homebrew. If Homebrew is not installed, follow the instructions at https://brew.sh.

## First-time setup

Do these steps only once on a new Mac.

### 1. Install the local tools

Open **Terminal** (Applications → Utilities → Terminal), then paste:

```bash
brew install colima docker docker-compose
```

### 2. Go to the backend folder

Use the actual folder where you saved the project. For example:

```bash
cd ~/Projects/dr-care-project/dr-care-backend
```

If your project is somewhere else, drag the `dr-care-backend` folder into the Terminal window after typing `cd `.

### 3. Create your private settings

Copy the safe templates:

```bash
cp .env.example .env
cp src/DrCare.Api/appsettings.Development.local.example.json src/DrCare.Api/appsettings.Development.local.json
```

The first file contains local database and login settings. The second file contains optional email settings.

Open them for editing:

```bash
open -e .env
open -e src/DrCare.Api/appsettings.Development.local.json
```

In `.env`, make sure these values are filled in:

```text
POSTGRES_PASSWORD=your-local-database-password
JWT_SIGNING_KEY=your-local-login-key-at-least-32-characters
DEVELOPMENT_ADMIN_PASSWORD=DrCareAdminPassword123!
STORAGE__PROVIDER=Local
LOCAL_STORAGE_SECRET=your-local-storage-secret
```

If you want to test email, fill in the Microsoft Graph values in `appsettings.Development.local.json`. If you are not testing email yet, you can leave that file out; the rest of the application will still run.

Do not edit the `.example` files. Do not share or commit `.env` or `appsettings.Development.local.json`; they contain machine-specific settings and credentials.

### 4. Start Colima

Paste this in Terminal:

```bash
colima start --cpu 4 --memory 8 --disk 60
```

Colima may take a minute the first time. It only needs to be started once per Mac restart.

### 5. Start the backend

Still in the `dr-care-backend` folder, paste:

```bash
docker context use colima
docker compose config --quiet
docker compose up -d --build
```

The first build can take several minutes. It starts the database, applies the database setup, starts the API, and keeps uploaded files in local storage.

Check that the API is ready by opening this address in a browser:

[http://localhost:8080/api/health/ready](http://localhost:8080/api/health/ready)

You should see `Healthy`.

### 6. Start the website

Open a **second Terminal window** and paste:

```bash
cd ~/Projects/dr-care-project/dr-care-frontend
npm install
npm run dev -- --host 0.0.0.0
```

Run `npm install` only the first time. Leave this Terminal window open while using the website.

Open the application:

[http://localhost:5173](http://localhost:5173)

## Daily start after the first setup

Each time you want to use Dr. Care:

### Terminal window 1 — backend

```bash
colima start
cd ~/Projects/dr-care-project/dr-care-backend
docker context use colima
docker compose up -d
```

### Terminal window 2 — website

```bash
cd ~/Projects/dr-care-project/dr-care-frontend
npm run dev -- --host 0.0.0.0
```

Then open [http://localhost:5173](http://localhost:5173).

If you changed backend code or email settings, use `docker compose up -d --build` instead of `docker compose up -d`.

## Test email sending

Email settings are read from:

`dr-care-backend/src/DrCare.Api/appsettings.Development.local.json`

After adding or changing those settings, rebuild the backend:

```bash
cd ~/Projects/dr-care-project/dr-care-backend
docker compose up -d --build
```

Then perform an action that sends email, such as creating a signing invitation or requesting a password reset. The application places the message in its queue first, then the background worker sends it through Microsoft Graph.

Your Microsoft Entra application must have Microsoft Graph **Mail.Send** application permission with admin consent, and the sender mailbox must be valid.

## Test accounts

On a new Development database, these accounts are created automatically. They all use the password in `DEVELOPMENT_ADMIN_PASSWORD` (for example, `DrCareAdminPassword123!`):

| Role | Email |
|---|---|
| Marketing Admin | `admin@drcare.local` |
| Marketing Agent | `marketing.agent@drcare.local` |
| General Manager | `general.manager@drcare.local` |
| Finance | `finance@drcare.local` |
| Admin Team | `admin.team@drcare.local` |
| Leadership | `leadership@drcare.local` |

## Stop the application

When finished:

1. In the frontend Terminal, press **Control + C**.
2. In the backend Terminal, run:

```bash
docker compose down
colima stop
```

Your database and uploaded files remain saved for the next run.

## If something goes wrong

### The `docker` command is not found

Run:

```bash
brew install colima docker docker-compose
```

### Docker cannot connect

Start Colima and select its context:

```bash
colima start
docker context use colima
```

### The API is not ready

View the backend message:

```bash
cd ~/Projects/dr-care-project/dr-care-backend
docker compose logs --tail=200 api
```

Most often, a value is missing from `.env`, or email is enabled without complete Microsoft Graph details.

### The website cannot open

Make sure the frontend Terminal is still running `npm run dev`. Then open [http://localhost:5173](http://localhost:5173) again.

### Email stays pending

Check the Microsoft Graph tenant, client, secret, sender mailbox, Mail.Send permission, and admin consent. Pending messages are kept in the database and can be retried.

## Important safety note

This setup is for local development only. Never use the shared development password or local JSON credentials in production. Never commit `.env` or `appsettings.Development.local.json`.


