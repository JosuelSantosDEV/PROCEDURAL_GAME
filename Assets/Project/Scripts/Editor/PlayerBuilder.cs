using System.Collections.Generic;
using PROCEDURAL_GAME.CameraControl;
using PROCEDURAL_GAME.Player;
using PROCEDURAL_GAME.UI;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PROCEDURAL_GAME.EditorTools
{
    /// <summary>
    /// Troca a cápsula do Player pelo personagem (modelo Fuse + animações Mixamo, copiados do projeto antigo para
    /// Assets/Project/Characters/Player). Na cena aberta: cria o material (URP Simple Lit com cor e normal), o Animator Controller
    /// (locomoção por velocidade parado -> correndo, pulo e queda), instancia o modelo como filho "Model" do Player, remove a cápsula
    /// visual e liga os scripts <see cref="PlayerAnimator"/> e <see cref="PlayerBodyVisibility"/>.
    /// Pode ser executado de novo: recria o material, o controller e o filho "Model".
    /// </summary>
    public static class PlayerBuilder
    {
        private const string Root = "Assets/Project/Characters/Player";
        private const string ModelPath = Root + "/Model/Player.fbx";
        private const string JumpPath = Root + "/Animations/Jump.fbx";

        // Andar para frente: Mixamo "Walking" (In Place), baixado pelo usuário. O Walk_Forward do pacote Burak não ficou bom e foi dispensado.
        private const string WalkForwardPath = Root + "/Animations/Walking.fbx";

        // Correr para frente: a do projeto antigo (Mixamo "Running"), a pedido do usuário. As outras 7 direções da corrida continuam do pacote Burak.
        private const string RunForwardPath = Root + "/Animations/Running.fbx";

        // Pulo PARADO: clipe que parte de um personagem parado (Mixamo, baixado pelo usuário). Pulando andando/correndo vale o Jump.fbx.
        private const string StandingJumpPath = Root + "/Animations/Jumping_2.fbx";

        // Sequência de ataque: gancho, chute (projeto antigo) e direto. Morte: projeto antigo. Gancho, direto, agachar e levantar: Mixamo, baixados pelo usuário.
        private const string HookPath = Root + "/Animations/Hook.fbx";
        private const string KickPath = Root + "/Animations/Chute.fbx";
        private const string CrossPath = Root + "/Animations/Cross Punch.fbx";
        private const string DeathPath = Root + "/Animations/Morte.fbx";
        private const string CrouchDownPath = Root + "/Animations/Standing To Crouched.fbx";
        private const string CrouchUpPath = Root + "/Animations/Crouched To Standing.fbx";

        // Pacote Burak Uzun (Asset Store, humanoide, in place): usado no lugar, sem copiar. Os 3 primeiros idles entram junto com o Idle do Player.
        private const string BurakIdlePath = "Assets/Burak Uzun/BasicCharacterAnimations/Character Animations/Idles/Idle_InPlace.fbx";
        // Só o "Bored" (uma vez só, depois de 4 voltas do idle antigo). Idle_InPlace e Idle_Alt_InPlace ficaram de fora a pedido do usuário.
        private static readonly string[] BurakIdleClips = { "Rig|Bored_InPlace" };

        // Idle ao parar: o antigo (Player.fbx) repete IdleLoops vezes e então toca o Bored uma vez; depois repete. Sem "memória": cada vez que
        // se entra em idle recomeça pelo antigo.
        private const int IdleLoops = 4;
        private const float IdleFade = 0.4f;

        // Andar e correr em 8 direções do mesmo pacote (versões In Place: quem move é o CharacterController, não a animação).
        private const string BurakMovementRoot = "Assets/Burak Uzun/BasicCharacterAnimations/Character Animations/Movement/";
        private const string BurakWalkPath = BurakMovementRoot + "Walk/Walk_InPlace.fbx";
        private const string BurakRunPath = BurakMovementRoot + "Run/Run_InPlace.fbx";

        // Direção de cada clipe (nome "Rig|<Walk|Run>_<direção>_InPlace") e posição na mistura 2D (x = direita, y = frente).
        private static readonly string[] DirectionNames = { "Forward", "Forward_Right", "Right", "Backward_Right", "Backward", "Backward_Left", "Left", "Forward_Left" };
        private static readonly Vector2[] DirectionPositions =
        {
            new Vector2(0f, 1f), new Vector2(0.7071f, 0.7071f), new Vector2(1f, 0f), new Vector2(0.7071f, -0.7071f),
            new Vector2(0f, -1f), new Vector2(-0.7071f, -0.7071f), new Vector2(-1f, 0f), new Vector2(-0.7071f, 0.7071f)
        };
        private const string DiffusePath = Root + "/Textures/Player_Diffuse.png";
        private const string NormalPath = Root + "/Textures/Player_Normal.png";
        private const string MaterialPath = Root + "/Materials/Player.mat";
        private const string ControllerPath = Root + "/Animations/PlayerAnimator.controller";
        private const string ModelChildName = "Model";
        private const string UndoName = "Setup Player Model";

        // Escala do personagem em relação ao tamanho original (cápsula de 2 m / modelo Fuse ≈ 1,8 m). Valor ÚNICO: muda o modelo,
        // o CharacterController e as distâncias da câmera juntos. Os valores base abaixo são absolutos (rodar o menu de novo não acumula).
        private const float PlayerScale = 2f;
        private const float BaseControllerHeight = 2f;
        // Cápsula esbelta (era 0,5, larga demais para um corpo humano): raio ≈ meia largura dos ombros + folga.
        private const float BaseControllerRadius = 0.3f;
        private const float BaseStepOffset = 0.3f;
        private const float BaseSkinWidth = 0.03f;
        private const float BaseEyeHeight = 0.6f;
        private const float BasePivotHeight = 1.2f;
        // 6 m e 0,35 m na escala 2x: a 8 m e 0,5 m, na floresta, quase sempre havia um tronco na frente.
        private const float BaseThirdPersonDistance = 3f;
        private const float BaseCameraCollisionRadius = 0.175f;
        // Limite de olhar para baixo em 1ª pessoa com corpo visível (graus). Era 60; o usuário pediu 5° a menos.
        private const float FirstPersonMaxPitch = 55f;

        // Avanço extra da câmera em 1ª pessoa com corpo visível, além dos olhos do modelo (escala 1; os olhos são medidos no osso da cabeça).
        private const float BaseFirstPersonForwardOffset = 0.05f;

        // Movimento (valores do jogo em escala 1, multiplicados por PlayerScale: mesma sensação de pulo e de queda do corpo menor).
        private const float BaseJumpHeight = 1.5f;
        private const float BaseGravity = -20f;
        private const float BaseGroundedStick = -2f;

        // Pulo em movimento (escala 1): 2,2 m e -22 m/s² na escala 2x. Sai do chão a ~10 m/s em vez de ~15,5 m/s e sobe em 0,45 s.
        private const float BaseMovingJumpHeight = 1.1f;
        private const float BaseMovingJumpGravity = -11f;

        // Fade da corrida para a pose do pulo (s). Com 0,05 s a pose trocava de uma vez.
        private const float MovingJumpEntryFade = 0.2f;

        // Tempo máximo (s) de agachar antes de um pulo parado sair do chão (mais que isso o toque parece atrasado).
        private const float StandingJumpMaxWindUp = 1f;

        // Duração de cada golpe na tela (s): a animação é acelerada ou freada para caber aqui, sem passar destes limites de velocidade.
        private const float HookTargetSeconds = 1.5f;
        private const float KickTargetSeconds = 1.1f;
        private const float CrossTargetSeconds = 0.9f;
        private const float MinAttackSpeed = 0.75f;
        private const float MaxAttackSpeed = 2f;

        // Fade entre um golpe e o seguinte (s). Igual ao AttackChainFade do PlayerActions.
        private const float AttackChainFade = 0.15f;

        // Fade de entrada do pulo parado (s): a preparação começa suave a partir do idle.
        private const float StandingJumpEntryFade = 0.15f;

        // Fade do pouso (s), do ar para idle/andar/correr. Começa no instante do toque, então não é atraso: é o tempo da mistura das poses.
        // 0,08 s parecia teleporte. Se mudar, ajuste também landingGaitRamp no PlayerAnimator.
        private const float LandingFade = 0.25f;

        // Velocidades aprovadas pelo usuário em teste no aparelho (m/s). Só mudam aqui se o ritmo dos passos ficar fora de
        // [MinCadence, MaxCadence] em relação à velocidade natural do clipe (medida em MeasureBodyVelocities): aí são trazidas para dentro
        // do limite, para os pés não deslizarem.
        private const float TunedWalkSpeed = 5.5f;
        private const float TunedSprintSpeed = 10f;
        private const float MinCadence = 0.75f;
        private const float MaxCadence = 1.35f;
        private const float FallbackWalkNaturalSpeed = 1.5f;
        private const float FallbackRunNaturalSpeed = 4.5f;

        [MenuItem("PROCEDURAL_GAME/Player/Setup Player Model")]
        public static void Build()
        {
            PlayerMovement movement = Object.FindObjectOfType<PlayerMovement>();
            if (movement == null)
            {
                Debug.LogError("Nenhum Player (PlayerMovement) na cena aberta. Abra Assets/Scenes/GameWorld.unity.");
                return;
            }

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"Modelo '{ModelPath}' não encontrado.");
                return;
            }

            Material material = EnsureMaterial();
            if (material == null) return;

            // Andar e correr em 8 direções (pacote Burak Uzun) e a velocidade natural de cada um (m/s medidos nos pés do clipe, na escala do Player).
            AnimationClip[] walkClips = CollectDirectionalClips(BurakWalkPath, "Walk");
            AnimationClip[] runClips = CollectDirectionalClips(BurakRunPath, "Run");
            if (walkClips == null || runClips == null) return;

            AnimationClip walkForward = EnsureMixamoClip(WalkForwardPath, true);
            AnimationClip runForward = EnsureMixamoClip(RunForwardPath, true);
            if (walkForward == null || runForward == null) return;
            walkClips[0] = walkForward; // posição 0 = Forward
            runClips[0] = runForward;

            float walkNatural = MeasureLocomotion(modelAsset, walkClips, "andar", FallbackWalkNaturalSpeed, out float[] walkScales) * PlayerScale;
            float runNatural = MeasureLocomotion(modelAsset, runClips, "correr", FallbackRunNaturalSpeed, out float[] runScales) * PlayerScale;
            float walkSpeed = RoundToHalf(Mathf.Clamp(TunedWalkSpeed, walkNatural * MinCadence, walkNatural * MaxCadence));
            float sprintSpeed = RoundToHalf(Mathf.Clamp(TunedSprintSpeed, runNatural * MinCadence, runNatural * MaxCadence));

            // Idles que se alternam parado: o do Player (índice 0, também usado na mistura parado -> correndo) + os do pacote Burak Uzun.
            List<AnimationClip> idles = CollectIdleClips();
            if (idles == null) return;

            // Pontos do clipe de pulo (pés saem do chão / ponto mais alto / pés voltam a tocar), medidos nos pés: o PlayerAnimator mapeia a
            // velocidade vertical para eles.
            JumpPhases jumpPhases = MeasureJumpPhases(modelAsset, FindClip(JumpPath));

            // Pulo parado (opcional: sem o arquivo, o pulo parado usa o clipe do pulo andando).
            AnimationClip standingJump = EnsureMixamoClip(StandingJumpPath, false);
            JumpPhases standingPhases = standingJump != null ? MeasureJumpPhases(modelAsset, standingJump) : default;
            if (standingJump == null) Debug.LogWarning("Pulo parado (Jumping_2.fbx) indisponível: o pulo parado usará o clipe do pulo andando.");

            // Preparação do pulo parado = o tempo real do agachar do clipe (até os pés saírem do chão): o corpo só sai do chão quando ela termina.
            float standingWindUp = 0f;
            if (standingJump != null)
            {
                float crouchTime = standingPhases.leave * standingPhases.clipLength;
                standingWindUp = crouchTime < 0.05f ? 0f : Mathf.Min(StandingJumpMaxWindUp, crouchTime);
                Debug.Log($"Pulo parado: {standingWindUp:0.00} s de preparação antes de sair do chão.");
            }

            ActionClips actionClips = new ActionClips
            {
                // O gancho avança e recua no próprio clipe: esse deslocamento precisa ficar na pose.
                hook = EnsureMixamoClip(HookPath, false, true),
                kick = EnsureMixamoClip(KickPath, false),
                cross = EnsureMixamoClip(CrossPath, false),
                death = EnsureMixamoClip(DeathPath, false),
                crouchDown = EnsureMixamoClip(CrouchDownPath, false),
                crouchUp = EnsureMixamoClip(CrouchUpPath, false)
            };
            if (actionClips.hook == null || actionClips.kick == null || actionClips.cross == null || actionClips.death == null || actionClips.crouchDown == null || actionClips.crouchUp == null) return;

            AnimatorController controller = EnsureController(walkSpeed, sprintSpeed, idles, walkClips, runClips, walkScales, runScales, jumpPhases, standingJump, standingPhases, actionClips);
            if (controller == null) return;

            Transform player = movement.transform;
            RemoveCapsuleVisual(player);

            Transform old = player.Find(ModelChildName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, player);
            Undo.RegisterCreatedObjectUndo(model, UndoName);
            model.name = ModelChildName;

            // Os pés do modelo ficam na base da cápsula do CharacterController (a origem do Mixamo é nos pés).
            CharacterController body = player.GetComponent<CharacterController>();
            ApplyScale(player, body);
            float feet = body != null ? body.center.y - body.height * 0.5f : -BaseControllerHeight * 0.5f * PlayerScale;
            model.transform.localPosition = new Vector3(0f, feet, 0f);
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * PlayerScale;

            Animator animator = model.GetComponent<Animator>();
            if (animator == null) animator = model.AddComponent<Animator>();
            if (animator.avatar == null) animator.avatar = LoadAvatar(ModelPath);
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;

            ConfigureRenderers(model, material);

            // Fica no objeto do Animator: o callback OnAnimatorIK só chega a ele.
            GetOrAdd<PlayerFootIK>(model.transform);

            ApplyMovement(movement, walkSpeed, sprintSpeed, standingWindUp);

            PlayerAnimator playerAnimator = GetOrAdd<PlayerAnimator>(player);
            SerializedObject animatorSo = new SerializedObject(playerAnimator);
            animatorSo.FindProperty("animator").objectReferenceValue = animator;
            animatorSo.FindProperty("minCadence").floatValue = 0.45f;
            animatorSo.FindProperty("jumpLeavePhase").floatValue = jumpPhases.leave;
            animatorSo.FindProperty("jumpApexPhase").floatValue = jumpPhases.apex;
            animatorSo.FindProperty("jumpLandPhase").floatValue = jumpPhases.land;
            animatorSo.FindProperty("jumpClipLength").floatValue = jumpPhases.clipLength;
            animatorSo.FindProperty("standingClipLength").floatValue = standingJump != null ? standingPhases.clipLength : 0f;
            if (standingJump != null)
            {
                animatorSo.FindProperty("standingLeavePhase").floatValue = standingPhases.leave;
                animatorSo.FindProperty("standingApexPhase").floatValue = standingPhases.apex;
                animatorSo.FindProperty("standingLandPhase").floatValue = standingPhases.land;
            }
            animatorSo.FindProperty("walkNaturalSpeed").floatValue = walkNatural;
            animatorSo.FindProperty("runNaturalSpeed").floatValue = runNatural;
            animatorSo.ApplyModifiedPropertiesWithoutUndo();

            // Sobrevivência: vida, energia, sede e fome (a tela é montada por código pelo SurvivalHud).
            GetOrAdd<PlayerStats>(player);

            PlayerActions playerActions = GetOrAdd<PlayerActions>(player);
            SerializedObject actionsSo = new SerializedObject(playerActions);
            actionsSo.FindProperty("animator").objectReferenceValue = animator;
            actionsSo.FindProperty("hookSeconds").floatValue = actionClips.hook.length / AttackSpeed(actionClips.hook, HookTargetSeconds);
            actionsSo.FindProperty("kickSeconds").floatValue = actionClips.kick.length / AttackSpeed(actionClips.kick, KickTargetSeconds);
            actionsSo.FindProperty("crossSeconds").floatValue = actionClips.cross.length / AttackSpeed(actionClips.cross, CrossTargetSeconds);
            actionsSo.FindProperty("deathSeconds").floatValue = actionClips.death.length;
            actionsSo.FindProperty("crouchDownSeconds").floatValue = actionClips.crouchDown.length;
            actionsSo.FindProperty("crouchUpSeconds").floatValue = actionClips.crouchUp.length;
            actionsSo.ApplyModifiedPropertiesWithoutUndo();

            GetOrAdd<SurvivalHud>(player);

            PlayerBodyVisibility visibility = GetOrAdd<PlayerBodyVisibility>(player);
            SerializedObject visibilitySo = new SerializedObject(visibility);
            visibilitySo.FindProperty("animator").objectReferenceValue = animator;
            visibilitySo.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
            Selection.activeGameObject = player.gameObject;
            Debug.Log($"Personagem do Player criado (escala {PlayerScale}x; andar {walkSpeed} m/s, correr {sprintSpeed} m/s). Salve a cena (Ctrl+S) e dê Play.");
        }

        // ---------------------------------------------------------------------------------------------------------------

        // O Player fica com escala 1 (o CharacterController e a câmera trabalham em metros do mundo e não escalam bem com o transform);
        // quem cresce é o filho "Model", a cápsula do CharacterController e as distâncias da câmera.
        private static void ApplyScale(Transform player, CharacterController body)
        {
            player.localScale = Vector3.one;

            if (body != null)
            {
                Undo.RecordObject(body, UndoName);
                body.height = BaseControllerHeight * PlayerScale;
                body.radius = BaseControllerRadius * PlayerScale;
                body.stepOffset = BaseStepOffset * PlayerScale;
                body.skinWidth = BaseSkinWidth * PlayerScale;
                body.center = Vector3.zero;
                EditorUtility.SetDirty(body);
            }

            PlayerCameraController cameraController = Object.FindObjectOfType<PlayerCameraController>();
            if (cameraController == null)
            {
                Debug.LogWarning("PlayerCameraController não encontrado na cena: ajuste altura dos olhos e distância da câmera à mão.");
                return;
            }

            SerializedObject cameraSo = new SerializedObject(cameraController);
            cameraSo.FindProperty("eyeHeight").floatValue = BaseEyeHeight * PlayerScale;
            cameraSo.FindProperty("pivotHeight").floatValue = BasePivotHeight * PlayerScale;
            cameraSo.FindProperty("thirdPersonDistance").floatValue = BaseThirdPersonDistance * PlayerScale;
            cameraSo.FindProperty("collisionRadius").floatValue = BaseCameraCollisionRadius * PlayerScale;
            cameraSo.FindProperty("firstPersonForwardOffset").floatValue = BaseFirstPersonForwardOffset * PlayerScale;
            cameraSo.FindProperty("firstPersonMaxPitch").floatValue = FirstPersonMaxPitch;
            cameraSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(cameraController);
        }

        // timeScales: cada clipe anda numa velocidade natural um pouco diferente (o frente do andar vem de outra fonte); a escala de tempo de
        // cada filho iguala o passo dele ao do clipe "frente" para a mistura entre direções não deslizar mais numa que na outra.
        private static void AddDirectionalTree(BlendTree parent, string name, float threshold, AnimationClip[] clips, float[] timeScales)
        {
            BlendTree tree = parent.CreateBlendTreeChild(threshold);
            tree.name = name;
            tree.blendType = BlendTreeType.FreeformDirectional2D;
            tree.blendParameter = "MoveX";
            tree.blendParameterY = "MoveY";
            tree.useAutomaticThresholds = false;

            for (int i = 0; i < clips.Length; i++) tree.AddChild(clips[i], DirectionPositions[i]);

            ChildMotion[] children = tree.children;
            for (int i = 0; i < children.Length && i < timeScales.Length; i++) children[i].timeScale = timeScales[i];
            tree.children = children;
        }

        /// <summary>
        /// Prepara um clipe do Mixamo (Animations/Walking.fbx ou Running.fbx): Humanoid com o Avatar do Player, em loop (ou não, conforme o clipe), sem root motion
        /// empurrando o Player. Só reimporta se ainda não estiver assim. Devolve o clipe (null com erro no Console se não achar o arquivo).
        /// </summary>
        // bakeRootXZ: o deslocamento horizontal do clipe fica na pose (o corpo avança e recua, a cápsula do Player não sai do lugar). Sem isso ele
        // vira root motion, que o jogo não aplica, e simplesmente some.
        private static AnimationClip EnsureMixamoClip(string path, bool loop, bool bakeRootXZ = false)
        {
            ModelImporter importer = AssetImporter.GetAtPath(path) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"'{path}' não encontrado. Baixe a animação (In Place, FBX for Unity) do Mixamo e salve nessa pasta.");
                return null;
            }

            Avatar avatar = LoadAvatar(ModelPath);
            if (avatar == null)
            {
                Debug.LogError($"Avatar do Player não encontrado em '{ModelPath}'. Espere a importação terminar e rode de novo.");
                return null;
            }

            if (importer.animationType != ModelImporterAnimationType.Human || importer.avatarSetup != ModelImporterAvatarSetup.CopyFromOther || importer.sourceAvatar != avatar)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
                importer.sourceAvatar = avatar;
                importer.importAnimation = true;
                importer.SaveAndReimport();
                importer = AssetImporter.GetAtPath(path) as ModelImporter;
            }

            ModelImporterClipAnimation[] clips = importer.clipAnimations;
            bool configured = clips != null && clips.Length > 0 && clips[0].loopTime == loop && clips[0].lockRootPositionXZ == bakeRootXZ;
            if (!configured)
            {
                clips = importer.defaultClipAnimations; // com os quadros reais do arquivo
                if (clips == null || clips.Length == 0)
                {
                    Debug.LogError($"'{path}' não tem animação. Baixe de novo do Mixamo (FBX for Unity, com ou sem pele, 30 fps).");
                    return null;
                }

                // Mesmos ajustes do Running do Mixamo: em loop, altura original, sem andar para o lado/frente pelo clipe.
                foreach (ModelImporterClipAnimation clip in clips)
                {
                    clip.loopTime = loop;
                    clip.lockRootRotation = true;
                    clip.lockRootHeightY = true;
                    clip.lockRootPositionXZ = bakeRootXZ;
                    clip.keepOriginalOrientation = !loop; // pulo (sem loop): como o Jump.fbx do projeto antigo
                    clip.keepOriginalPositionY = true;
                    clip.keepOriginalPositionXZ = bakeRootXZ;
                }

                importer.clipAnimations = clips;
                importer.SaveAndReimport();
            }

            AnimationClip walk = FindClip(path);
            if (walk == null) Debug.LogError($"Clipe de '{path}' não encontrado depois de reimportar. Rode o menu de novo.");
            return walk;
        }

        // Os 8 clipes de uma marcha (Walk ou Run) na ordem de DirectionNames; null (com erro no Console) se algum faltar.
        private static AnimationClip[] CollectDirectionalClips(string fbxPath, string gait)
        {
            AnimationClip[] clips = new AnimationClip[DirectionNames.Length];
            for (int i = 0; i < clips.Length; i++)
            {
                string clipName = $"Rig|{gait}_{DirectionNames[i]}_InPlace";
                clips[i] = FindClipByName(fbxPath, clipName);
                if (clips[i] == null)
                {
                    Debug.LogError($"Clipe '{clipName}' não encontrado em '{fbxPath}'. Espere a importação terminar e rode de novo.");
                    return null;
                }
            }

            return clips;
        }

        // Velocidades e pulo proporcionais à escala (mesmo tempo no ar) e andar/correr casados com o passo da animação.
        private static void ApplyMovement(PlayerMovement movement, float walkSpeed, float sprintSpeed, float standingWindUp)
        {
            SerializedObject so = new SerializedObject(movement);
            so.FindProperty("walkSpeed").floatValue = walkSpeed;
            so.FindProperty("sprintSpeed").floatValue = sprintSpeed;
            so.FindProperty("jumpHeight").floatValue = BaseJumpHeight * PlayerScale;
            so.FindProperty("gravity").floatValue = BaseGravity * PlayerScale;
            so.FindProperty("movingJumpHeight").floatValue = BaseMovingJumpHeight * PlayerScale;
            so.FindProperty("movingJumpGravity").floatValue = BaseMovingJumpGravity * PlayerScale;
            so.FindProperty("groundedStickVelocity").floatValue = BaseGroundedStick * PlayerScale;
            so.FindProperty("standingJumpWindUp").floatValue = standingWindUp;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(movement);
        }

        private static float RoundToHalf(float value) => Mathf.Round(value * 2f) * 0.5f;

        /// <summary>
        /// Velocidade natural (m/s, na escala 1 do modelo) da marcha: a do clipe "frente". Mede também os outros 7 e avisa no Console se
        /// algum anda para uma direção diferente da do nome (esquerda/direita trocadas, por exemplo). Se não der para medir, usa o valor típico.
        /// </summary>
        private static float MeasureLocomotion(GameObject modelAsset, AnimationClip[] clips, string label, float fallback, out float[] timeScales)
        {
            timeScales = new float[clips.Length];
            for (int i = 0; i < timeScales.Length; i++) timeScales[i] = 1f;

            Vector2[] velocities = MeasureBodyVelocities(modelAsset, clips);
            if (velocities == null || velocities[0].magnitude < 0.3f)
            {
                Debug.LogWarning($"Não consegui medir a velocidade natural de {label}; usando {fallback} m/s. Se os pés deslizarem, ajuste walkSpeed/sprintSpeed.");
                return fallback;
            }

            for (int i = 0; i < clips.Length; i++)
            {
                Vector2 measured = velocities[i];
                if (measured.magnitude < 0.2f) continue; // parado ou inconclusivo: não opina

                if (Vector2.Dot(measured.normalized, DirectionPositions[i]) < 0.5f)
                {
                    Debug.LogWarning($"'{clips[i].name}' parece ir para outra direção (medido x={measured.x:0.00}, z={measured.y:0.00} m/s; esperado {DirectionPositions[i]}). Confira o resultado em jogo.");
                }
            }

            float natural = velocities[0].magnitude;

            // Iguala o passo dos outros clipes ao do "frente" (limitado: um clipe muito diferente não é esticado além disso).
            for (int i = 1; i < clips.Length; i++)
            {
                if (velocities[i].magnitude >= 0.3f) timeScales[i] = Mathf.Clamp(natural / velocities[i].magnitude, 0.7f, 1.4f);
            }

            Debug.Log($"Natural de {label}: {natural:0.00} m/s no corpo em escala 1 ({natural * PlayerScale:0.0} m/s em escala {PlayerScale}).");
            return natural;
        }

        /// <summary>
        /// Amostra cada clipe num modelo temporário e, enquanto cada pé está apoiado (perto do ponto mais baixo dele), vê para onde ele recua
        /// em relação ao quadril: o oposto disso é a velocidade do corpo no espaço do modelo (x = direita, y = frente), em m/s (mediana).
        /// Devolve null se o modelo não puder ser medido; vetor zero para o clipe sem pé apoiado reconhecível.
        /// </summary>
        private static Vector2[] MeasureBodyVelocities(GameObject modelAsset, AnimationClip[] clips)
        {
            const int Samples = 60;
            const float StanceHeight = 0.05f;

            GameObject temp = null;
            try
            {
                temp = (GameObject)Object.Instantiate(modelAsset);
                temp.hideFlags = HideFlags.HideAndDontSave;

                Animator probe = temp.GetComponent<Animator>();
                if (probe == null || !probe.isHuman) throw new System.InvalidOperationException("modelo sem Animator humanoide");

                Transform hips = probe.GetBoneTransform(HumanBodyBones.Hips);
                Transform leftFoot = probe.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform rightFoot = probe.GetBoneTransform(HumanBodyBones.RightFoot);
                if (hips == null || leftFoot == null || rightFoot == null) throw new System.InvalidOperationException("ossos não encontrados");

                Vector2[] result = new Vector2[clips.Length];
                for (int c = 0; c < clips.Length; c++)
                {
                    AnimationClip clip = clips[c];
                    float dt = clip.length / Samples;
                    Vector3[] left = new Vector3[Samples + 1];
                    Vector3[] right = new Vector3[Samples + 1];

                    for (int i = 0; i <= Samples; i++)
                    {
                        clip.SampleAnimation(temp, i * dt);
                        Vector3 hipsLocal = temp.transform.InverseTransformPoint(hips.position);
                        left[i] = RelativeToHips(temp.transform, leftFoot, hipsLocal);
                        right[i] = RelativeToHips(temp.transform, rightFoot, hipsLocal);
                    }

                    List<float> xs = new List<float>();
                    List<float> zs = new List<float>();
                    CollectStanceVelocities(left, dt, StanceHeight, xs, zs);
                    CollectStanceVelocities(right, dt, StanceHeight, xs, zs);

                    if (xs.Count < 6) continue;

                    xs.Sort();
                    zs.Sort();
                    result[c] = new Vector2(xs[xs.Count / 2], zs[zs.Count / 2]);
                }

                return result;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Medição das animações falhou: {e.Message}");
                return null;
            }
            finally
            {
                if (temp != null) Object.DestroyImmediate(temp);
            }
        }

        // Posição do pé no espaço do modelo, com o plano horizontal relativo ao quadril (o clipe pode ou não carregar root motion).
        private static Vector3 RelativeToHips(Transform model, Transform foot, Vector3 hipsLocal)
        {
            Vector3 local = model.InverseTransformPoint(foot.position);
            return new Vector3(local.x - hipsLocal.x, local.y, local.z - hipsLocal.z);
        }

        // Em cada intervalo com o pé baixo (apoiado), o recuo do pé é a velocidade do corpo no sentido contrário.
        private static void CollectStanceVelocities(Vector3[] foot, float dt, float stanceHeight, List<float> xs, List<float> zs)
        {
            float lowest = float.MaxValue;
            foreach (Vector3 p in foot) lowest = Mathf.Min(lowest, p.y);

            for (int i = 0; i + 1 < foot.Length; i++)
            {
                if (foot[i].y > lowest + stanceHeight || foot[i + 1].y > lowest + stanceHeight) continue;

                float vx = -(foot[i + 1].x - foot[i].x) / dt;
                float vz = -(foot[i + 1].z - foot[i].z) / dt;
                if (vx * vx + vz * vz < 0.04f) continue; // pé parado no clipe: sem informação

                xs.Add(vx);
                zs.Add(vz);
            }
        }

        private static Material EnsureMaterial()
        {
            Texture2D diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(DiffusePath);
            Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
            if (diffuse == null)
            {
                Debug.LogError($"Textura '{DiffusePath}' não encontrada.");
                return null;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
            {
                Debug.LogError("Shader 'Universal Render Pipeline/Simple Lit' não encontrado (o projeto usa URP?).");
                return null;
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader) { name = "Player" };
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", diffuse);
            material.SetColor("_BaseColor", Color.white);

            // Pouco brilho: pele e roupa; o brilho forte do Simple Lit destoaria da neblina.
            material.SetColor("_SpecColor", new Color(0.05f, 0.05f, 0.05f, 1f));
            material.SetFloat("_Smoothness", 0.1f);

            if (normal != null)
            {
                material.SetTexture("_BumpMap", normal);
                material.EnableKeyword("_NORMALMAP");
            }

            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssets();
            return material;
        }

        private static List<AnimationClip> CollectIdleClips()
        {
            List<AnimationClip> clips = new List<AnimationClip>();

            AnimationClip own = FindClip(ModelPath);
            if (own == null)
            {
                Debug.LogError($"Idle do Player não encontrado em '{ModelPath}'. Espere a importação terminar e rode de novo.");
                return null;
            }

            clips.Add(own);

            foreach (string clipName in BurakIdleClips)
            {
                AnimationClip clip = FindClipByName(BurakIdlePath, clipName);
                if (clip == null)
                {
                    Debug.LogWarning($"Idle '{clipName}' não encontrado em '{BurakIdlePath}': fica de fora da alternância.");
                    continue;
                }

                clips.Add(clip);
            }

            if (clips.Count < 2) Debug.LogWarning("Só o idle do Player foi encontrado: não haverá alternância de idles.");
            return clips;
        }

        private static AnimatorController EnsureController(float walkSpeed, float sprintSpeed, List<AnimationClip> idles, AnimationClip[] walkClips, AnimationClip[] runClips, float[] walkScales, float[] runScales, JumpPhases jumpPhases, AnimationClip standingJump, JumpPhases standingPhases, ActionClips actionClips)
        {
            AnimationClip idle = idles[0];
            AnimationClip jump = FindClip(JumpPath);
            if (idle == null || jump == null)
            {
                Debug.LogError($"Animações não encontradas (Idle em '{ModelPath}', Jump em '{JumpPath}'). Espere a importação terminar e rode de novo.");
                return null;
            }

            // Recria do zero: o controller é todo gerado aqui.
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null) AssetDatabase.DeleteAsset(ControllerPath);
            AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            // Posição na mistura de marcha: 0 idle, 1 andar, 2 correr (o PlayerAnimator calcula; qualquer movimento já chega em "andar").
            controller.AddParameter("Gait", AnimatorControllerParameterType.Float);
            // Direção do movimento em relação ao corpo (MoveX = direita, MoveY = frente), para a mistura de 8 direções.
            controller.AddParameter("MoveX", AnimatorControllerParameterType.Float);
            controller.AddParameter(new AnimatorControllerParameter { name = "MoveY", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter(new AnimatorControllerParameter { name = "AnimSpeed", type = AnimatorControllerParameterType.Float, defaultFloat = 1f });
            controller.AddParameter(new AnimatorControllerParameter { name = "Grounded", type = AnimatorControllerParameterType.Bool, defaultBool = true });
            controller.AddParameter("Jump", AnimatorControllerParameterType.Trigger);
            // O pulo que saiu foi o PARADO (clipe que parte de um personagem parado) ou o andando/correndo.
            controller.AddParameter("StandingJump", AnimatorControllerParameterType.Bool);
            // Pousou (o PlayerAnimator liga ao tocar o chão depois de um pulo/queda e desliga ao voltar ao ar): o estado de ar sai na hora.
            controller.AddParameter("Landed", AnimatorControllerParameterType.Bool);
            // Ações que prendem o corpo (PlayerActions).
            controller.AddParameter("Hook", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Kick", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Cross", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Collect", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("Die", AnimatorControllerParameterType.Trigger);
            // Ponto (0..1) do clipe de pulo que o estado Air mostra; o PlayerAnimator o calcula pela velocidade vertical e, ao pousar, o leva até o fim.
            controller.AddParameter(new AnimatorControllerParameter { name = "AirPhase", type = AnimatorControllerParameterType.Float, defaultFloat = 0.3f });

            AnimatorStateMachine machine = controller.layers[0].stateMachine;

            // Locomoção: parado -> correndo pela velocidade; AnimSpeed ajusta a cadência dos passos.
            AnimatorState locomotion = controller.CreateBlendTreeInController("Locomotion", out BlendTree tree, 0);
            tree.blendType = BlendTreeType.Simple1D;
            tree.blendParameter = "Gait";
            tree.useAutomaticThresholds = false;
            // Raiz por velocidade: parado -> andar -> correr; cada faixa é uma mistura 2D de 8 direções (frente, diagonais, lados, trás).
            tree.AddChild(idle, 0f);
            AddDirectionalTree(tree, "Walk", 1f, walkClips, walkScales);
            AddDirectionalTree(tree, "Run", 2f, runClips, runScales);
            locomotion.speedParameterActive = true;
            locomotion.speedParameter = "AnimSpeed";
            machine.defaultState = locomotion;

            // No ar (pulo OU queda): UM estado que toca o clipe de pulo com o tempo vindo do parâmetro AirPhase (calculado pelo PlayerAnimator
            // pela velocidade vertical), sem relógio próprio. Assim pulo curto (subida), longo (descida) e queda de borda ficam coerentes.
            AnimatorState air = machine.AddState("Air");
            air.motion = jump;
            air.speed = 0f;
            air.timeParameterActive = true;
            air.timeParameter = "AirPhase";

            // Mesmo mecanismo para o pulo PARADO, com o clipe que parte de um personagem parado e as fases dele.
            AnimatorState airStanding = null;
            if (standingJump != null)
            {
                airStanding = machine.AddState("AirStanding");
                airStanding.motion = standingJump;
                airStanding.speed = 0f;
                airStanding.timeParameterActive = true;
                airStanding.timeParameter = "AirPhase";
            }

            // Locomoção -> ar: gatilho de pulo (andando/correndo; ou parado se não há clipe de pulo parado) ou sem chão (queda de borda, que usa o do pulo andando).
            AddJumpTransitions(locomotion, air, airStanding);

            // Ações: antes da saída para o idle (a primeira transição que vale é a usada, e a ação zera o Speed no mesmo frame).
            ActionStates actionStates = BuildActionStates(machine, actionClips);
            AddActionTriggers(locomotion, actionStates);

            AnimatorStateTransition toFall = locomotion.AddTransition(air);
            toFall.hasExitTime = false;
            toFall.duration = 0.1f;
            toFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");

            BuildIdleStates(machine, locomotion, air, airStanding, idles, actionStates, actionClips);

            // IK dos pés (PlayerFootIK): o callback OnAnimatorIK só roda com "IK Pass" ligado na camada.
            AnimatorControllerLayer[] layers = controller.layers;
            layers[0].iKPass = true;
            controller.layers = layers;

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            return controller;
        }

        // Idle: dois estados. Idle0 (o antigo) repete IdleLoops vezes e passa ao Idle1 (Bored, uma vez só), que volta ao Idle0, e assim por diante.
        // Tudo é nativo do Animator (tempo de estado com exit time), sem script e SEM memória: toda entrada em idle vem pelo Idle0, e o tempo
        // do estado recomeça do zero. Mexer-se, pular ou cair sai de qualquer um dos dois. Também liga os estados de ar (pulo/queda).
        private static void BuildIdleStates(AnimatorStateMachine machine, AnimatorState locomotion, AnimatorState air, AnimatorState airStanding, List<AnimationClip> idles, ActionStates actionStates, ActionClips actionClips)
        {
            const float StopSpeed = 0.1f;
            const float MoveSpeed = 0.15f;

            AnimatorState[] states = new AnimatorState[idles.Count];
            for (int i = 0; i < idles.Count; i++)
            {
                states[i] = machine.AddState($"Idle{i}");
                states[i].motion = idles[i];
            }

            for (int i = 0; i < states.Length; i++)
            {
                AddActionTriggers(states[i], actionStates);

                // Saídas por movimento, pulo e queda primeiro: têm prioridade sobre o ciclo de idle.
                AnimatorStateTransition move = states[i].AddTransition(locomotion);
                move.hasExitTime = false;
                move.duration = 0.1f; // curto: um toque rápido na manete ainda mostra o passo
                move.AddCondition(AnimatorConditionMode.Greater, MoveSpeed, "Speed");

                AddJumpTransitions(states[i], air, airStanding);

                AnimatorStateTransition toFall = states[i].AddTransition(air);
                toFall.hasExitTime = false;
                toFall.duration = 0.1f;
                toFall.AddCondition(AnimatorConditionMode.IfNot, 0f, "Grounded");
            }

            // Ciclo: Idle0 -> Idle1 depois de IdleLoops voltas (o fade começa um pouco antes para o Bored entrar quando a 4ª volta acaba);
            // Idle1 -> Idle0 quando o Bored termina.
            if (states.Length > 1)
            {
                float length0 = Mathf.Max(0.1f, idles[0].length);
                AnimatorStateTransition toBored = states[0].AddTransition(states[1]);
                toBored.hasExitTime = true;
                toBored.exitTime = Mathf.Max(IdleLoops - 1f, IdleLoops - IdleFade / length0);
                toBored.hasFixedDuration = true;
                toBored.duration = IdleFade;
                toBored.interruptionSource = TransitionInterruptionSource.Source; // pular ou andar no meio do fade não espera ele terminar

                float length1 = Mathf.Max(0.1f, idles[1].length);
                AnimatorStateTransition backToIdle = states[1].AddTransition(states[0]);
                backToIdle.hasExitTime = true;
                backToIdle.exitTime = Mathf.Clamp(1f - IdleFade / length1, 0.5f, 1f);
                backToIdle.hasFixedDuration = true;
                backToIdle.duration = IdleFade;
                backToIdle.interruptionSource = TransitionInterruptionSource.Source;
            }

            // Parar (locomoção -> idle): sempre pelo Idle0.
            AnimatorStateTransition stop = locomotion.AddTransition(states[0]);
            stop.hasExitTime = false;
            stop.duration = 0.12f; // curto: ao soltar a manete o idle entra rápido
            stop.interruptionSource = TransitionInterruptionSource.Source;
            stop.AddCondition(AnimatorConditionMode.Less, StopSpeed, "Speed");
            stop.AddCondition(AnimatorConditionMode.If, 0f, "Grounded");

            // Ar -> chão SEM ESPERA: no instante em que os pés tocam (Landed) vai para o que a manete pede: parado = idle, andando = andar,
            // correndo = correr (a mistura andar/correr já está na marcha e na direção certas, porque o PlayerAnimator as atualiza no ar).
            AddLandings(air, locomotion, states[0], StopSpeed, MoveSpeed);
            if (airStanding != null) AddLandings(airStanding, locomotion, states[0], StopSpeed, MoveSpeed);

            // Fim das ações: volta ao idle (se a manete estiver inclinada, o Idle0 sai logo para a locomoção).
            AddExit(actionStates.hook, states[0], actionClips.hook, true);
            AddExit(actionStates.kick, states[0], actionClips.kick, true);
            AddExit(actionStates.cross, states[0], actionClips.cross, true);
            AddExit(actionStates.crouchUp, states[0], actionClips.crouchUp);
        }

        private struct ActionClips
        {
            public AnimationClip hook;
            public AnimationClip kick;
            public AnimationClip cross;
            public AnimationClip death;
            public AnimationClip crouchDown;
            public AnimationClip crouchUp;
        }

        private struct ActionStates
        {
            public AnimatorState hook;
            public AnimatorState kick;
            public AnimatorState cross;
            public AnimatorState crouchDown;
            public AnimatorState crouchUp;
        }

        private static ActionStates BuildActionStates(AnimatorStateMachine machine, ActionClips clips)
        {
            ActionStates states = new ActionStates
            {
                hook = AddClipState(machine, "Hook", clips.hook),
                kick = AddClipState(machine, "Kick", clips.kick),
                cross = AddClipState(machine, "Cross", clips.cross),
                crouchDown = AddClipState(machine, "CrouchDown", clips.crouchDown),
                crouchUp = AddClipState(machine, "CrouchUp", clips.crouchUp)
            };

            states.hook.speed = AttackSpeed(clips.hook, HookTargetSeconds);
            states.kick.speed = AttackSpeed(clips.kick, KickTargetSeconds);
            states.cross.speed = AttackSpeed(clips.cross, CrossTargetSeconds);
            Debug.Log($"Golpes: gancho {clips.hook.length:0.00} s x{states.hook.speed:0.00}, chute {clips.kick.length:0.00} s x{states.kick.speed:0.00}, direto {clips.cross.length:0.00} s x{states.cross.speed:0.00}.");

            // Encadear golpes: de um golpe direto ao seguinte, sem passar pelo idle.
            AddTrigger(states.hook, states.kick, "Kick", AttackChainFade);
            AddTrigger(states.hook, states.cross, "Cross", AttackChainFade);
            AddTrigger(states.kick, states.cross, "Cross", AttackChainFade);
            AddTrigger(states.kick, states.hook, "Hook", AttackChainFade);
            AddTrigger(states.cross, states.hook, "Hook", AttackChainFade);
            AddTrigger(states.cross, states.kick, "Kick", AttackChainFade);

            AddExit(states.crouchDown, states.crouchUp, clips.crouchDown);

            // Morrer vale de qualquer estado e não tem saída: a pose final fica até a tela de morte.
            AnimatorState death = AddClipState(machine, "Death", clips.death);
            AnimatorStateTransition die = machine.AddAnyStateTransition(death);
            die.canTransitionToSelf = false;
            die.hasExitTime = false;
            die.duration = 0.15f;
            die.AddCondition(AnimatorConditionMode.If, 0f, "Die");

            return states;
        }

        private static AnimatorState AddClipState(AnimatorStateMachine machine, string name, AnimationClip clip)
        {
            AnimatorState state = machine.AddState(name);
            state.motion = clip;
            return state;
        }

        private static void AddActionTriggers(AnimatorState from, ActionStates states)
        {
            AddTrigger(from, states.hook, "Hook");
            AddTrigger(from, states.kick, "Kick");
            AddTrigger(from, states.cross, "Cross");
            AddTrigger(from, states.crouchDown, "Collect");
        }

        private static float AttackSpeed(AnimationClip clip, float targetSeconds) => Mathf.Clamp(clip.length / targetSeconds, MinAttackSpeed, MaxAttackSpeed);

        private static void AddTrigger(AnimatorState from, AnimatorState to, string trigger, float duration = 0.1f)
        {
            AnimatorStateTransition transition = from.AddTransition(to);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = duration;
            transition.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        }

        // Sai com um fade que termina junto com o clipe.
        // interruptible: o próximo golpe pode entrar no meio deste fade.
        private static void AddExit(AnimatorState from, AnimatorState to, AnimationClip clip, bool interruptible = false)
        {
            const float Fade = 0.15f;

            AnimatorStateTransition exit = from.AddTransition(to);
            exit.hasExitTime = true;
            exit.exitTime = Mathf.Clamp(1f - Fade / Mathf.Max(0.1f, clip.length / Mathf.Max(0.01f, from.speed)), 0.5f, 1f);
            exit.hasFixedDuration = true;
            exit.duration = Fade;
            if (interruptible) exit.interruptionSource = TransitionInterruptionSource.Source;
        }

        private static void AddLandings(AnimatorState airState, AnimatorState locomotion, AnimatorState idleEntry, float stopSpeed, float moveSpeed)
        {
            AnimatorStateTransition toIdle = airState.AddTransition(idleEntry);
            toIdle.hasExitTime = false;
            toIdle.duration = LandingFade;
            toIdle.AddCondition(AnimatorConditionMode.If, 0f, "Landed");
            toIdle.AddCondition(AnimatorConditionMode.Less, stopSpeed, "Speed");

            AnimatorStateTransition toMove = airState.AddTransition(locomotion);
            toMove.hasExitTime = false;
            toMove.duration = LandingFade;
            toMove.AddCondition(AnimatorConditionMode.If, 0f, "Landed");
            toMove.AddCondition(AnimatorConditionMode.Greater, moveSpeed, "Speed");

            // Velocidade entre os dois limites (um instante de hesitação da manete): também sai, para a locomoção.
            AnimatorStateTransition any = airState.AddTransition(locomotion);
            any.hasExitTime = false;
            any.duration = LandingFade;
            any.AddCondition(AnimatorConditionMode.If, 0f, "Landed");
        }

        // Transições de "pulo pedido" a partir de um estado: com clipe de pulo parado, o gatilho vai para o estado certo conforme StandingJump
        // (parado = AirStanding, andando/correndo = Air); sem ele, sempre Air.
        private static void AddJumpTransitions(AnimatorState from, AnimatorState air, AnimatorState airStanding)
        {
            AnimatorStateTransition toMoving = from.AddTransition(air);
            toMoving.hasExitTime = false;
            toMoving.hasFixedDuration = true;
            toMoving.duration = MovingJumpEntryFade;
            toMoving.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            if (airStanding != null) toMoving.AddCondition(AnimatorConditionMode.IfNot, 0f, "StandingJump");

            if (airStanding == null) return;

            AnimatorStateTransition toStanding = from.AddTransition(airStanding);
            toStanding.hasExitTime = false;
            toStanding.hasFixedDuration = true;
            toStanding.duration = StandingJumpEntryFade;
            toStanding.AddCondition(AnimatorConditionMode.If, 0f, "Jump");
            toStanding.AddCondition(AnimatorConditionMode.If, 0f, "StandingJump");
        }

        private struct JumpPhases
        {
            public float leave;
            public float apex;
            public float land;
            public float clipLength;
        }

        /// <summary>
        /// Acha no clipe de pulo (em fração 0..1) quando os pés saem do chão, quando estão mais altos e quando voltam a tocar, olhando a altura do
        /// pé mais baixo num modelo temporário. Se não der para medir, usa valores típicos de um pulo do Mixamo.
        /// </summary>
        private static JumpPhases MeasureJumpPhases(GameObject modelAsset, AnimationClip clip)
        {
            JumpPhases fallback = new JumpPhases { leave = 0.3f, apex = 0.5f, land = 0.75f, clipLength = clip != null ? clip.length : 0.87f };
            if (clip == null) return fallback;

            const int Samples = 60;
            const float AirHeight = 0.06f; // m na escala 1: pé mais baixo acima disto = no ar

            GameObject temp = null;
            try
            {
                temp = (GameObject)Object.Instantiate(modelAsset);
                temp.hideFlags = HideFlags.HideAndDontSave;

                Animator probe = temp.GetComponent<Animator>();
                if (probe == null || !probe.isHuman) throw new System.InvalidOperationException("modelo sem Animator humanoide");

                Transform left = probe.GetBoneTransform(HumanBodyBones.LeftFoot);
                Transform right = probe.GetBoneTransform(HumanBodyBones.RightFoot);
                if (left == null || right == null) throw new System.InvalidOperationException("ossos dos pés não encontrados");

                float[] height = new float[Samples + 1];
                float ground = float.MaxValue;
                int apexIndex = 0;

                for (int i = 0; i <= Samples; i++)
                {
                    clip.SampleAnimation(temp, clip.length * i / Samples);
                    height[i] = Mathf.Min(left.position.y, right.position.y);
                    ground = Mathf.Min(ground, height[i]);
                }

                for (int i = 1; i <= Samples; i++)
                {
                    if (height[i] > height[apexIndex]) apexIndex = i;
                }

                int leaveIndex = -1;
                for (int i = 0; i <= Samples; i++)
                {
                    if (height[i] > ground + AirHeight)
                    {
                        leaveIndex = i;
                        break;
                    }
                }

                int landIndex = -1;
                for (int i = apexIndex; i <= Samples; i++)
                {
                    if (height[i] <= ground + AirHeight)
                    {
                        landIndex = i;
                        break;
                    }
                }

                if (leaveIndex < 0 || apexIndex <= leaveIndex || landIndex <= apexIndex) throw new System.InvalidOperationException($"pulo sem fase de ar reconhecível (saída {leaveIndex}, ápice {apexIndex}, pouso {landIndex})");

                JumpPhases phases = new JumpPhases
                {
                    leave = (float)leaveIndex / Samples,
                    apex = (float)apexIndex / Samples,
                    land = (float)landIndex / Samples,
                    clipLength = clip.length
                };

                Debug.Log($"Clipe de pulo ({clip.length:0.00} s): pés saem em {phases.leave:0.00}, ponto mais alto em {phases.apex:0.00}, tocam o chão em {phases.land:0.00}.");
                return phases;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"Não consegui medir as fases do pulo ({e.Message}); usando valores típicos (saída 0,30 / ápice 0,50 / pouso 0,75).");
                return fallback;
            }
            finally
            {
                if (temp != null) Object.DestroyImmediate(temp);
            }
        }

        private static AnimationClip FindClipByName(string fbxPath, string clipName)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is AnimationClip clip && clip.name == clipName) return clip;
            }

            return null;
        }

        private static AnimationClip FindClip(string fbxPath)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                // O FBX também traz clipes "__preview__" internos do Unity.
                if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__")) return clip;
            }

            return null;
        }

        private static Avatar LoadAvatar(string fbxPath)
        {
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            {
                if (asset is Avatar avatar) return avatar;
            }

            return null;
        }

        // A cápsula do Player tinha malha e renderizador só para aparecer; o CharacterController continua.
        private static void RemoveCapsuleVisual(Transform player)
        {
            MeshRenderer meshRenderer = player.GetComponent<MeshRenderer>();
            if (meshRenderer != null) Undo.DestroyObjectImmediate(meshRenderer);

            MeshFilter meshFilter = player.GetComponent<MeshFilter>();
            if (meshFilter != null) Undo.DestroyObjectImmediate(meshFilter);
        }

        // O modelo tem uma malha por peça (corpo, olhos, cílios, cabelo, roupas) e todas usam o mesmo material (atlas único).
        private static void ConfigureRenderers(GameObject model, Material material)
        {
            List<SkinnedMeshRenderer> renderers = new List<SkinnedMeshRenderer>(model.GetComponentsInChildren<SkinnedMeshRenderer>(true));

            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Material[] materials = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;

                renderer.sharedMaterials = materials;
                renderer.updateWhenOffscreen = false;
                renderer.skinnedMotionVectors = false;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            Debug.Log($"Modelo do Player: {renderers.Count} malha(s) com o material do personagem.");
        }

        private static T GetOrAdd<T>(Transform target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : Undo.AddComponent<T>(target.gameObject);
        }
    }
}
