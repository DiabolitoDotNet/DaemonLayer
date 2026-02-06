# Secrets directory
# Place your sensitive configuration here

# telegram_bot_token.txt - Your Telegram bot token from @BotFather
# telegram_user_ids.txt - Comma-separated list of allowed Telegram user IDs

# email_smtp.json - SMTP settings for the email_send tool (JSON)
# Mounted in docker as: /run/secrets/email_smtp_json

# Docker Compose
# docker-compose.yml mounts these files as Docker secrets:
#   /run/secrets/telegram_bot_token
#   /run/secrets/telegram_user_ids
# The Host will automatically read them at startup when Telegram:* is not set via config/env vars.

# Email SMTP (recommended: user-secrets)
# Use the helper script to store Email:* settings in the Host project's user-secrets (no credentials committed):
#   powershell -ExecutionPolicy Bypass -File .\scripts\set-email-user-secrets.ps1

# Example:
# echo "123456789:ABCdefGHIjklMNOpqrSTUvwxYZ" > telegram_bot_token.txt
# echo "123456789,987654321" > telegram_user_ids.txt

# Helper (recommended)
# If you already stored Telegram settings in .NET user-secrets, export them into docker secret files
# without printing the token:
#   powershell -ExecutionPolicy Bypass -File .\scripts\export-telegram-docker-secrets.ps1 -Force

# Export Email SMTP user-secrets to docker secret JSON (recommended):
#   powershell -ExecutionPolicy Bypass -File .\scripts\export-email-docker-secret.ps1 -Force
