# PROCEDURAL_GAME — contexto do projeto

## Visão geral
- Jogo **mobile** (Android/iOS) com **geração procedural de ambientes**.
- Projeto novo, construído aos poucos. Os 3 scripts iniciais vieram de outro projeto ("IndustrialProject"); referências a ele devem ser removidas se aparecerem.
- Idioma: conversa e comentários de código em **português (BR)**; nomes de classes/variáveis em inglês.

## Stack
- Unity **2022.3.12f1**, URP 14.0.9, **Input System 1.7.0** (`activeInputHandler: 1` = só o novo Input System; não usar `UnityEngine.Input`).
- Cena principal: `Assets/Scenes/GameWorld.unity` (Terrain 50x50 em (-5,0,-5), Player = cápsula em (0,1,0), Main Camera).

## Estrutura
- `Assets/Project/Scripts/Player/` — `PlayerInputReader` (lê Actions), `PlayerMovement` (CharacterController: andar, correr, pular, gravidade).
- `Assets/Project/Scripts/Camera/` — `PlayerCameraController` (1ª/3ª pessoa, colisão de câmera). Fica na **Main Camera**.
- `Assets/Project/Input/PlayerInputActions.inputactions` — mapa `Player`: Move, Look, Jump, Sprint, ToggleCamera.
- `Assets/Project/Scripts/MobileInput/` — controles de toque: `MobileControls` (agrega tudo, fica no Canvas), `TouchButton`, `TouchLookArea` (arrastar para olhar). O `PlayerInputReader` lê daqui (campo `Mobile Controls`) junto com teclado/gamepad.
- `Assets/Project/Scripts/Editor/MobileControlsBuilder.cs` — menu **PROCEDURAL_GAME > Create Mobile Controls**: (re)cria Canvas + EventSystem (InputSystemUIInputModule) e liga ao Player/câmera. Para mudar o layout da UI, edite este script e rode de novo.
- `Assets/PinePie/Simple Joystick/` — pacote de terceiros (Asset Store). Usamos só o `JoystickController` (modo Floating) e os sprites. Alterações locais: `using UnityEditor` dentro de `#if UNITY_EDITOR` (senão quebra o build mobile) e campo novo `keepVisible` (base do joystick visível na posição inicial em Floating/Dynamic). Pasta `Examples` foi removida.
- Namespaces: `PROCEDURAL_GAME.Player`, `PROCEDURAL_GAME.CameraControl`, `PROCEDURAL_GAME.MobileInput`, `PROCEDURAL_GAME.EditorTools`.

## Controles atuais (teste no PC)
WASD mover · Mouse olhar · Espaço pular · Shift correr · **V** alternar 1ª/3ª pessoa · Esc libera o cursor (clique devolve).
Gamepad: stick esquerdo/direito, A (pular), L3 (correr), R3 (alternar câmera).

## Controles de toque (mobile)
Esquerda da tela: joystick flutuante (Move) · Direita: arrastar o dedo gira a câmera; botões Pular, Correr (segurar) e Câmera. Novo botão (ex.: Ataque) = novo `TouchButton` em `MobileControls` + propriedade no `PlayerInputReader` + botão em `MobileControlsBuilder`.
- `PlayerMovement` e `PlayerCameraController` têm `[DefaultExecutionOrder(100)]` para ler os toques depois do EventSystem (ordem 0) no mesmo frame.
- A câmera tem a opção `Lock Cursor`: desmarcada (estado atual da cena) permite testar o toque com o mouse no Editor, mas o olhar por mouse/WASD+mouse no PC não gira a câmera; marque para o modo PC antigo.
- Joystick: zona de toque = metade esquerda; o pivô da zona e a âncora da base ficam no canto inferior esquerdo (exigência do modo Floating do pacote: pivô do pai == âncora da base). Posição de repouso em `JoystickRestPosition` no builder.
- Câmera 3ª pessoa: `thirdPersonMinPitch` (-30°) limita olhar para cima (senão a câmera vai para baixo do chão) e `KeepOutsideBody` impede a câmera de entrar na cápsula do Player (o SphereCast ignora o Player de propósito).
- Dev Android: Xiaomi (HyperOS) precisa de "Instalar via USB" ligado. `AndroidNdkEnvironment.cs` (Editor) define `ANDROID_NDK_ROOT` para o Burst achar o NDK no build.
- Multi-toque (joystick + arrastar ao mesmo tempo) só é testável em aparelho real.

## Convenções
- Player precisa da tag `Player`; usa `CharacterController` (sem CapsuleCollider).
- Scripts separam entrada (`PlayerInputReader`) de lógica (`PlayerMovement`, `PlayerCameraController`).
- Ao editar cenas/prefabs em YAML com o Editor aberto, o Unity pede para recarregar a cena.

## Estado / próximos passos
- [x] Scripts corrigidos, Input Actions criado, componentes ligados na cena (mover, pular, alternar câmera).
- [x] Controles de toque (joystick virtual, arrastar para olhar, botões Pular/Correr/Câmera) — código pronto; falta o usuário rodar o menu `Create Mobile Controls` e validar em aparelho.
- [ ] Botão de ataque (e outros) — a adicionar depois.
- [ ] Geração procedural de ambiente — ainda não iniciada.
