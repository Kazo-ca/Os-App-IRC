> [English version](README.md)

# Os-App-IRC

Client de discussion IRC natif pour [Kazo OS](https://github.com/Kazo-ca/Os), écrit en C# et
Blazor, pour les vrais réseaux IRC sur ordinateur et mobile. Il se connecte par WebSocket sans
aucun serveur, et par le relais du serveur optionnel de Kazo OS (TCP avec TLS) aux réseaux qui
n'acceptent pas WebSocket.

**État :** en développement (série `irc/base`). L'app s'ouvre dans Kazo OS avec sa mise en page
(trois colonnes sur ordinateur, une colonne avec des panneaux sur téléphone) et l'option de police
console ; le protocole IRC et la connexion arrivent avec les chantiers suivants (voir le tableau
d'avancement du plan).

- Plan : [Français](doc/fr/plan.md) · [English](doc/en/plan.md)
- Architecture du code : [Français](doc/fr/architecture.md) · [English](doc/en/architecture.md)
- Licence : MIT ([LICENSE](LICENSE)) ; œuvres tierces : [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Compiler et lancer

```bash
git clone --recurse-submodules https://github.com/Kazo-ca/Os-App-IRC.git
cd Os-App-IRC
dotnet run --project src/Kazo.Irc.Os      # Kazo OS avec l'app IRC, sur http://localhost:5310
```

Le sous-module `external/KazoOs` est Kazo OS sur son tag de release (3.0.0) : il donne la version
des paquets Kazo OS. `Debug` compile l'OS depuis les sources du sous-module, `Release` prend les
paquets de nuget.org (ce que compilent la CI et le Kazo OS Store).

## Tests

```bash
dotnet build Kazo.Irc.slnx -c Release
dotnet test Kazo.Irc.slnx -c Release --no-build
npm ci && npx playwright install chromium
npm run test:e2e                            # l'app dans l'hôte Kazo OS, ordinateur et téléphone
```
