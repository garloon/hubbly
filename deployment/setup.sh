#!/bin/bash
set -e

echo "=========================================="
echo "Hubbly Deployment Setup Script"
echo "=========================================="
echo ""

# Check if running as root
if [ "$EUID" -ne 0 ]; then
    echo "This script must be run as root. Please use sudo."
    exit 1
fi

# Update system
echo "[1/7] Updating system packages..."
apt-get update && apt-get upgrade -y

# Install Docker if not installed
echo "[2/7] Installing Docker..."
if ! command -v docker &> /dev/null; then
    curl -fsSL https://get.docker.com -o get-docker.sh
    sh get-docker.sh
    systemctl start docker
    systemctl enable docker
    rm get-docker.sh
else
    echo "Docker already installed"
fi

# Install Docker Compose if not installed
echo "[3/7] Installing Docker Compose..."
if ! command -v docker-compose &> /dev/null; then
    curl -L "https://github.com/docker/compose/releases/latest/download/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
    chmod +x /usr/local/bin/docker-compose
else
    echo "Docker Compose already installed"
fi

# Create deployment directory if not exists
echo "[4/7] Setting up deployment directory..."
# Files are already copied via SCP to /opt/hubbly, just cd to directory
cd /opt/hubbly || { echo "Directory /opt/hubbly not found"; exit 1; }

# Create .env file from example if not exists
if [ ! -f docker/.env ]; then
    echo "Creating default .env file..."
    cat > docker/.env << 'EOF'
POSTGRES_PASSWORD=hubbly_password_123
JWT_SECRET=hubbly-super-secure-jwt-secret-key-2026-32chars
CORS_ALLOWED_ORIGINS=http://localhost:5000,http://127.0.0.1:5000,http://10.0.2.2:5000,http://192.168.0.103:5000,http://89.169.46.33:5000
ASPNETCORE_ENVIRONMENT=Production
EOF
    echo "⚠️  Please change default passwords in docker/.env"
fi

# Login to Docker Hub (if credentials provided)
echo "[5/7] Docker Hub authentication..."
if [ -n "$DOCKER_USERNAME" ] && [ -n "$DOCKER_PASSWORD" ]; then
    echo "$DOCKER_PASSWORD" | docker login -u "$DOCKER_USERNAME" --password-stdin
else
    echo "⚠️  DOCKER_USERNAME and DOCKER_PASSWORD not set, skipping Docker Hub login"
fi

# Pull Docker images
echo "[6/7] Pulling Docker images..."
docker-compose -f docker-compose.yml pull

# Stop and remove existing containers if they exist
echo "Cleaning up old containers..."
docker-compose -f docker-compose.yml down 2>/dev/null || true
docker rm -f hubbly-postgres hubbly-api 2>/dev/null || true

# Initialize database
echo "[6/7] Initializing database..."
docker-compose -f docker-compose.yml up -d postgres
sleep 10
docker-compose -f docker-compose.yml exec -T postgres psql -U hubbly -d hubbly -f /docker-entrypoint-initdb.d/init-db.sql

# Start all services
echo "[7/7] Starting all services..."
docker-compose -f docker-compose.yml up -d

echo ""
echo "=========================================="
echo "Setup Complete!"
echo "=========================================="
echo ""
echo "Services running:"
echo "  📱 API:              http://89.169.46.33:5000"
echo "  🗄️  PostgreSQL:       localhost:5432"
echo ""
echo "To view logs:"
echo "  docker-compose -f docker-compose.yml logs -f"
echo ""
echo "To stop:"
echo "  docker-compose -f docker-compose.yml down"
echo ""
echo "To restart:"
echo "  docker-compose -f docker-compose.yml restart"
echo ""
