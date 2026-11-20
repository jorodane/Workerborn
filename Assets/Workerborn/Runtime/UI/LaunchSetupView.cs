using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Workerborn
{
    public sealed class LaunchSetupView : MonoBehaviour
    {
        [SerializeField] private GameObject worldPanel;
        [SerializeField] private GameObject characterPanel;
        [SerializeField] private TMP_Text worldChoice;
        [SerializeField] private TMP_Text characterChoice;
        [SerializeField] private TMP_Text body;
        [SerializeField] private TMP_Text hair;
        [SerializeField] private TMP_Text outfit;
        [SerializeField] private TMP_Text face;
        [SerializeField] private TMP_Text status;
        [SerializeField] private TMP_InputField worldName;
        [SerializeField] private TMP_InputField characterName;
        [SerializeField] private Button previousWorld;
        [SerializeField] private Button nextWorld;
        [SerializeField] private Button newWorld;
        [SerializeField] private Button useWorld;
        [SerializeField] private Button previousCharacter;
        [SerializeField] private Button nextCharacter;
        [SerializeField] private Button newCharacter;
        [SerializeField] private Button launch;
        [SerializeField] private Button bodyCycle;
        [SerializeField] private Button hairCycle;
        [SerializeField] private Button outfitCycle;
        [SerializeField] private Button faceCycle;
        [SerializeField] private RawImage portrait;

        private LaunchDirectory directory;
        private List<LocalWorld> worlds;
        private LocalWorld world;
        private PortableCharacter character;
        private Action<LocalWorld, PortableCharacter> start;
        private LaunchCharacterPreview preview;
        private int worldIndex, characterIndex;
        private bool selectingWorld;

        private void Awake()
        {
            previousWorld.onClick.AddListener(() => ChooseWorld(-1));
            nextWorld.onClick.AddListener(() => ChooseWorld(1));
            newWorld.onClick.AddListener(NewWorld);
            useWorld.onClick.AddListener(() =>
            {
                if (world == null)
                    return;
                if (worldIndex < 0)
                    world.name = PortableCharacter.CleanName(worldName.text, "솔바람 마을");
                ShowCharacter();
            });
            previousCharacter.onClick.AddListener(() => ChooseCharacter(-1));
            nextCharacter.onClick.AddListener(() => ChooseCharacter(1));
            newCharacter.onClick.AddListener(() => SetCharacter(-1));
            bodyCycle.onClick.AddListener(() =>
            {
                character.look.body = 1 - character.look.body;
                UpdateLook();
            });
            hairCycle.onClick.AddListener(() =>
            {
                character.look.hair = character.look.hair % CharacterLook.HairCount + 1;
                UpdateLook();
            });
            outfitCycle.onClick.AddListener(() =>
            {
                character.look.outfit = character.look.outfit % CharacterLook.OutfitCount + 1;
                UpdateLook();
            });
            faceCycle.onClick.AddListener(() =>
            {
                character.look.face = character.look.face % CharacterLook.FaceCount + 1;
                UpdateLook();
            });
            launch.onClick.AddListener(Launch);
        }

        internal void Open(LaunchDirectory catalog, List<LocalWorld> choices, Action<LocalWorld, PortableCharacter> begin)
        {
            directory = catalog;
            worlds = choices;
            start = begin;
            preview = FindFirstObjectByType<LaunchCharacterPreview>();
            if (preview != null)
                portrait.texture = preview.Texture;
            status.text = "";
            world = null;
            character = null;
            if (worlds == null)
            {
                ShowCharacter();
                return;
            }
            if (worlds.Count > 0)
            {
                worldIndex = 0;
                world = worlds[worldIndex];
            }
            else
                NewWorld();
            ShowWorld();
        }

        internal bool Back()
        {
            if (worlds == null || selectingWorld)
                return false;
            ShowWorld();
            return true;
        }

        internal void SetBusy(bool busy)
        {
            launch.interactable = !busy;
            characterName.interactable = !busy;
            bodyCycle.interactable = hairCycle.interactable = outfitCycle.interactable = faceCycle.interactable = !busy;
            previousCharacter.interactable = nextCharacter.interactable = !busy && directory != null && directory.characters.Count > 0;
            newCharacter.interactable = !busy;
        }

        private void ShowWorld()
        {
            selectingWorld = true;
            worldPanel.SetActive(true);
            characterPanel.SetActive(false);
            preview?.Hide();
            UpdateWorldChoice();
        }

        private void ChooseWorld(int direction)
        {
            if (worlds.Count == 0)
                return;
            worldIndex = worldIndex < 0 ? (direction > 0 ? 0 : worlds.Count - 1)
                : (worldIndex + direction + worlds.Count) % worlds.Count;
            world = worlds[worldIndex];
            UpdateWorldChoice();
        }

        private void NewWorld()
        {
            worldIndex = -1;
            world = LocalWorld.New();
            UpdateWorldChoice();
        }

        private void UpdateWorldChoice()
        {
            bool draft = worldIndex < 0;
            worldChoice.text = draft ? "새 월드" : world.name;
            worldName.gameObject.SetActive(draft);
            if (draft)
                worldName.SetTextWithoutNotify(world.name);
            previousWorld.interactable = nextWorld.interactable = worlds.Count > 0;
        }

        private void ShowCharacter()
        {
            selectingWorld = false;
            worldPanel.SetActive(false);
            characterPanel.SetActive(true);
            if (character == null)
                SetCharacter(directory.characters.Count > 0 ? 0 : -1);
            else
                UpdateLook();
        }

        private void ChooseCharacter(int direction)
        {
            int count = directory.characters.Count;
            if (count > 0)
                SetCharacter(characterIndex < 0 ? (direction > 0 ? 0 : count - 1) : (characterIndex + direction + count) % count);
        }

        private void SetCharacter(int index)
        {
            characterIndex = index;
            if (index < 0)
            {
                character = PortableCharacter.New();
                if (preview != null)
                    character.look = preview.DefaultLook;
            }
            else
            {
                var saved = directory.characters[index];
                character = new PortableCharacter { id = saved.id, name = saved.name, look = saved.look.Validated() };
            }
            characterChoice.text = index < 0 ? "새 캐릭터" : $"{index + 1}/{directory.characters.Count}  {character.name}";
            characterName.SetTextWithoutNotify(character.name);
            UpdateLook();
        }

        private void UpdateLook()
        {
            var look = character.look.Validated();
            body.text = look.body == 0 ? "체형 1" : "체형 2";
            hair.text = $"머리 {look.hair:00}";
            outfit.text = $"의상 {look.outfit:00}";
            face.text = $"얼굴 {look.face:00}";
            preview?.Show(look);
        }

        private void Launch()
        {
            if (character == null)
                return;
            character.name = PortableCharacter.CleanName(characterName.text, "일꾼");
            character.look = character.look.Validated();
            if (!directory.Store(world, character, out var error))
            {
                status.text = "월드와 캐릭터 선택을 저장하지 못했어요.";
                Debug.LogWarning(error);
                return;
            }
            status.text = "";
            start(world, character);
        }

        private void OnDisable() => preview?.Hide();
    }
}
