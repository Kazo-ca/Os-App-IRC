> [English version](README.md)

# Os-App-IRC

Client de discussion IRC natif pour [Kazo OS](https://github.com/Kazo-ca/Os), écrit en C# et
Blazor, pour les vrais réseaux IRC sur ordinateur et mobile. Il se connecte par WebSocket sans
aucun serveur, et par le relais du serveur optionnel de Kazo OS (TCP avec TLS) aux réseaux qui
n'acceptent pas WebSocket.

**État :** en développement (série `irc/base`). La bibliothèque du protocole IRC est écrite
(chantier 2), et l'app s'ouvre dans Kazo OS avec sa mise en page (trois colonnes sur ordinateur,
une colonne avec des panneaux sur téléphone) et l'option de police console (chantier 1). La
discussion est branchée à la fenêtre (chantier 4) : dans un build Debug, le réseau « Kazo Demo », un
serveur IRC dans l'app, montre les conversations, les mentions avec notification de l'OS et le titre
« (N) IRC », et les journaux par jour dans `~/.local/share/ca.kazo.irc/logs/`. Les vrais réseaux
sont joints depuis le chantier 3 : **sans aucun serveur** par WebSocket (UnrealIRCd est proposé, et
tout réseau qui accepte IRCv3 par WebSocket peut être ajouté), et **par le serveur Kazo** pour les
autres réseaux (Libera.Chat et OFTC sont proposés), avec TLS sur 6697. Un administrateur approuve le
vrai réseau au premier lancement d'IRC. Les réseaux se modifient dans l'app (« Gérer les réseaux »,
`~/.config/ca.kazo.irc/networks.json`) ; un mot de passe SASL est gardé dans le trousseau de l'OS.
Voir le tableau d'avancement du plan.

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
