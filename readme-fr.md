> [English version](README.md)

# Os-App-IRC

Client de discussion IRC natif pour [Kazo OS](https://github.com/Kazo-ca/Os), écrit en C# et
Blazor, pour les vrais réseaux IRC sur ordinateur et mobile. Il se connecte par WebSocket sans
aucun serveur, et par le relais du serveur optionnel de Kazo OS (TCP avec TLS) aux réseaux qui
n'acceptent pas WebSocket.

**Version 1.0.0**, pour Kazo OS 3.2.0 ou plus récent, sur ordinateur et sur téléphone. Il se
connecte **sans aucun serveur** par WebSocket (UnrealIRCd est proposé, et tout réseau qui accepte
IRCv3 par WebSocket peut être ajouté) et **par le serveur Kazo** aux autres réseaux (Libera.Chat et
OFTC sont proposés), avec TLS sur 6697. Un administrateur approuve le vrai réseau au premier
lancement d'IRC. Les mentions et messages privés donnent une notification de l'OS et le titre
« (N) IRC », les adresses web s'ouvrent dans le Navigateur de Kazo OS, les discussions sont
journalisées par jour dans `~/.local/share/ca.kazo.irc/logs/`, les réseaux se modifient dans l'app
(« Gérer les réseaux », `~/.config/ca.kazo.irc/networks.json`) et un mot de passe SASL est gardé
dans le trousseau de l'OS.

**Installer :** depuis le Kazo OS Store (catégorie Communication), ou dans le Terminal de Kazo OS en
administrateur : `sudo kazopkg install Kazo-ca/Os-App-IRC`. La fiche du Store est dans
[`store/`](store).

- Plan : [Français](doc/fr/plan.md) · [English](doc/en/plan.md)
- Architecture du code : [Français](doc/fr/architecture.md) · [English](doc/en/architecture.md)
- Licence : MIT ([LICENSE](LICENSE)) ; œuvres tierces : [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Compiler et lancer

```bash
git clone --recurse-submodules https://github.com/Kazo-ca/Os-App-IRC.git
cd Os-App-IRC
dotnet run --project src/Kazo.Irc.Os      # Kazo OS avec l'app IRC, sur http://localhost:5310
```

Le sous-module `external/KazoOs` est Kazo OS sur son tag de release (3.2.0) : il donne la version
des paquets Kazo OS. `Debug` compile l'OS depuis les sources du sous-module, `Release` prend les
paquets de nuget.org (ce que compilent la CI et le Kazo OS Store).

## Tests

```bash
dotnet build Kazo.Irc.slnx -c Release -p:IrcDemoNetwork=true   # les tests E2E utilisent le réseau de démonstration
dotnet test Kazo.Irc.slnx -c Release --no-build
npm ci && npx playwright install chromium
npm run test:e2e                            # l'app dans l'hôte Kazo OS, ordinateur et téléphone
```

Le build du Store n'a pas le réseau de démonstration, et met la bibliothèque du protocole dans
`Kazo.Irc.dll` (Kazo OS 3.2.0 met une assembly par app dans un paquet,
[Os#419](https://github.com/Kazo-ca/Os/issues/419)) :

```bash
dotnet build src/Kazo.Irc -c Release -t:KazoOsPack       # le paquet, dans src/Kazo.Irc/bin/kazoos-packages
npm run test:installed -- <wwwroot de Kazo OS> src/Kazo.Irc/bin/kazoos-packages   # installé par kazopkg, dans un Kazo OS publié
dotnet build src/Kazo.Irc.Os -c Release && npm run store:screenshots             # store/screenshots/
```

Voir « Fiche du Kazo OS Store et app installée » dans l'[architecture](doc/fr/architecture.md).
