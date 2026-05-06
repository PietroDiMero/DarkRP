#!/bin/bash
# ============================================================
#  DarkRP S&Box — Script de démarrage
#  Démarre le sidecar PHP (→ MySQL) PUIS le serveur S&Box.
#
#  Dans WISP/Pterodactyl :
#    Startup Command → bash start.sh
#    (ou remplacez votre commande actuelle par ce script)
# ============================================================

# Chemin vers le dossier darkapi (relatif au dossier de travail)
DARKAPI_DIR="$(dirname "$0")/darkapi"
DARKAPI_PORT=9000

echo "[DarkRP] Démarrage du sidecar MySQL (PHP)..."

# Vérifier que PHP est installé
if ! command -v php &> /dev/null; then
    echo "[DarkRP] ❌ PHP introuvable ! Installez-le avec :"
    echo "          apt-get install -y php-cli php-mysql"
    exit 1
fi

# Vérifier que l'extension PDO MySQL est disponible
if ! php -r "new PDO('mysql:host=localhost', 'x', 'x');" 2>/dev/null | grep -q ""; then
    php -m | grep -q "pdo_mysql" || {
        echo "[DarkRP] ⚠️  Extension pdo_mysql manquante. Essayez : apt-get install php-mysql"
    }
fi

# Démarrer le sidecar PHP en arrière-plan
php -S "127.0.0.1:${DARKAPI_PORT}" "${DARKAPI_DIR}/index.php" \
    >> /tmp/darkrp_api.log 2>&1 &

PHP_PID=$!
echo "[DarkRP] ✅ Sidecar PHP démarré (PID: ${PHP_PID}, port: ${DARKAPI_PORT})"

# Laisser 2 secondes au PHP pour démarrer
sleep 2

# Vérifier que le sidecar est bien en ligne
if ! curl -s -f "http://127.0.0.1:${DARKAPI_PORT}/ping" \
     -H "X-Api-Key: 349c7e8efdb530c7fae5b294be087d43da63fb45d827d621bb4657d07480c5a0" > /dev/null; then
    echo "[DarkRP] ⚠️  Le sidecar ne répond pas — vérifiez /tmp/darkrp_api.log"
    # On continue quand même (S&Box affichera une erreur dans ses logs)
fi

echo "[DarkRP] Démarrage du serveur S&Box..."

exec ./sbox-server.exe \
    +hostname "DarkRP FR | RP Downtown" \
    +game sousoup.darkrp2 \
    +map thieves.rpdowntown3t \
    +maxplayers 32
