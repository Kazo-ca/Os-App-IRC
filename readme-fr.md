> [English version](README.md)

# Os-App-IRC

Client de discussion IRC natif pour [Kazo OS](https://github.com/Kazo-ca/Os), écrit en C# et
Blazor, pour les vrais réseaux IRC sur ordinateur et mobile. Il se connecte par WebSocket sans
aucun serveur, et par le relais du serveur optionnel de Kazo OS (TCP avec TLS) aux réseaux qui
n'acceptent pas WebSocket.

La bibliothèque du protocole IRC est écrite (chantier 2) ; l'app est en construction (série `irc/base`).

- Plan : [Français](doc/fr/plan.md) · [English](doc/en/plan.md)
- Licence : MIT ([LICENSE](LICENSE)).
