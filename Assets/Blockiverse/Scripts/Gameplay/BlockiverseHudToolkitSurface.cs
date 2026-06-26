using UnityEngine;
using UnityEngine.UIElements;

namespace Blockiverse.Gameplay
{
    [DisallowMultipleComponent]
    public sealed class BlockiverseHudToolkitSurface : MonoBehaviour
    {
        [SerializeField] UIDocument document;
        [SerializeField] bool hideOnAwake = true;

        VisualElement root;
        Label healthValueLabel;
        ProgressBar healthBar;
        Label healthStateLabel;
        Label statusLabel;
        Label hotbarLabel;
        ProgressBar miningProgressBar;
        VisualElement fadeOverlay;
        float toastVisibleUntil;

        public string CurrentStatusText => statusLabel != null ? statusLabel.text : string.Empty;
        public string CurrentHealthText => healthValueLabel != null ? healthValueLabel.text : string.Empty;
        public float CurrentHealthValue => healthBar != null ? healthBar.value : 0.0f;
        public float CurrentMiningProgress => miningProgressBar != null ? miningProgressBar.value : 0.0f;
        public bool IsMiningProgressVisible => miningProgressBar != null && miningProgressBar.resolvedStyle.display != DisplayStyle.None;
        public bool IsVisible => root != null && root.resolvedStyle.display != DisplayStyle.None;

        public void Configure(UIDocument targetDocument)
        {
            document = targetDocument;
            BuildIfNeeded();
            if (hideOnAwake)
                SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            BuildIfNeeded();
            if (root != null)
                root.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetHealth(int current, int max, string state)
        {
            BuildIfNeeded();
            int safeMax = Mathf.Max(1, max);
            if (healthValueLabel != null)
                healthValueLabel.text = $"{Mathf.Clamp(current, 0, safeMax)} / {safeMax}";
            if (healthBar != null)
            {
                healthBar.lowValue = 0.0f;
                healthBar.highValue = safeMax;
                healthBar.value = Mathf.Clamp(current, 0, safeMax);
            }
            if (healthStateLabel != null)
                healthStateLabel.text = state ?? string.Empty;
        }

        public void SetStatus(string message)
        {
            BuildIfNeeded();
            if (statusLabel == null)
                return;

            statusLabel.text = message ?? string.Empty;
            statusLabel.style.display = string.IsNullOrEmpty(statusLabel.text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        public void ShowToast(string message, float visibleSeconds)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            SetStatus(message);
            toastVisibleUntil = Time.unscaledTime + Mathf.Max(0.1f, visibleSeconds);
        }

        public void SetMiningProgress(float progress, bool visible)
        {
            BuildIfNeeded();
            if (miningProgressBar == null)
                return;

            miningProgressBar.lowValue = 0.0f;
            miningProgressBar.highValue = 1.0f;
            miningProgressBar.value = Mathf.Clamp01(progress);
            miningProgressBar.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetSelectedBlock(string label)
        {
            BuildIfNeeded();
            if (hotbarLabel != null)
                hotbarLabel.text = label ?? string.Empty;
        }

        public void SetHotbarVisible(bool visible)
        {
            BuildIfNeeded();
            if (hotbarLabel != null)
                hotbarLabel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        public void SetComfortFade(float alpha)
        {
            BuildIfNeeded();
            if (fadeOverlay == null)
                return;

            float clamped = Mathf.Clamp01(alpha);
            fadeOverlay.style.opacity = clamped;
            fadeOverlay.style.display = clamped > 0.001f ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Awake()
        {
            if (document == null)
                document = GetComponent<UIDocument>();

            BuildIfNeeded();
            if (hideOnAwake)
                SetVisible(false);
        }

        void Update()
        {
            if (toastVisibleUntil <= 0.0f || Time.unscaledTime < toastVisibleUntil)
                return;

            toastVisibleUntil = 0.0f;
            SetStatus(string.Empty);
        }

        void BuildIfNeeded()
        {
            if (document == null)
                document = GetComponent<UIDocument>();
            if (document == null || document.rootVisualElement == null)
                return;
            if (root != null && root.panel == document.rootVisualElement.panel)
                return;

            VisualElement documentRoot = document.rootVisualElement;
            documentRoot.Clear();
            documentRoot.style.width = Length.Percent(100.0f);
            documentRoot.style.height = Length.Percent(100.0f);

            root = new VisualElement { name = "blockiverse-hud-root" };
            root.style.flexGrow = 1.0f;
            root.style.paddingLeft = 24.0f;
            root.style.paddingTop = 24.0f;
            root.style.paddingRight = 24.0f;
            root.style.paddingBottom = 24.0f;
            root.style.justifyContent = Justify.SpaceBetween;
            root.style.color = Color.white;

            var topRow = new VisualElement { name = "blockiverse-hud-top" };
            topRow.style.flexDirection = FlexDirection.Row;
            topRow.style.justifyContent = Justify.SpaceBetween;
            topRow.style.alignItems = Align.FlexStart;

            var healthPanel = new VisualElement { name = "blockiverse-hud-health" };
            healthPanel.style.width = 280.0f;
            healthPanel.style.paddingLeft = 16.0f;
            healthPanel.style.paddingRight = 16.0f;
            healthPanel.style.paddingTop = 12.0f;
            healthPanel.style.paddingBottom = 12.0f;
            healthPanel.style.backgroundColor = new Color(0.02f, 0.08f, 0.11f, 0.88f);

            var healthTitle = new Label("Health");
            healthTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            healthTitle.style.fontSize = 18.0f;
            healthPanel.Add(healthTitle);

            healthValueLabel = new Label("100 / 100");
            healthValueLabel.name = "blockiverse-hud-health-value";
            healthValueLabel.style.fontSize = 22.0f;
            healthPanel.Add(healthValueLabel);

            healthBar = new ProgressBar { name = "blockiverse-hud-health-bar", lowValue = 0.0f, highValue = 100.0f, value = 100.0f };
            healthBar.style.height = 14.0f;
            healthPanel.Add(healthBar);

            healthStateLabel = new Label("Stable");
            healthStateLabel.name = "blockiverse-hud-health-state";
            healthStateLabel.style.fontSize = 15.0f;
            healthPanel.Add(healthStateLabel);

            statusLabel = new Label();
            statusLabel.name = "blockiverse-hud-status";
            statusLabel.style.fontSize = 20.0f;
            statusLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            statusLabel.style.display = DisplayStyle.None;
            topRow.Add(healthPanel);
            topRow.Add(statusLabel);

            hotbarLabel = new Label("No block");
            hotbarLabel.name = "blockiverse-hud-hotbar";
            hotbarLabel.style.alignSelf = Align.Center;
            hotbarLabel.style.fontSize = 20.0f;
            hotbarLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            hotbarLabel.style.paddingLeft = 18.0f;
            hotbarLabel.style.paddingRight = 18.0f;
            hotbarLabel.style.paddingTop = 10.0f;
            hotbarLabel.style.paddingBottom = 10.0f;
            hotbarLabel.style.backgroundColor = new Color(0.02f, 0.08f, 0.11f, 0.88f);
            hotbarLabel.style.display = DisplayStyle.None;

            miningProgressBar = new ProgressBar { name = "blockiverse-hud-mining", lowValue = 0.0f, highValue = 1.0f };
            miningProgressBar.style.alignSelf = Align.Center;
            miningProgressBar.style.width = 300.0f;
            miningProgressBar.style.height = 16.0f;
            miningProgressBar.style.display = DisplayStyle.None;

            fadeOverlay = new VisualElement { name = "blockiverse-hud-comfort-fade" };
            fadeOverlay.style.position = Position.Absolute;
            fadeOverlay.style.left = 0.0f;
            fadeOverlay.style.right = 0.0f;
            fadeOverlay.style.top = 0.0f;
            fadeOverlay.style.bottom = 0.0f;
            fadeOverlay.style.backgroundColor = Color.black;
            fadeOverlay.style.opacity = 0.0f;
            fadeOverlay.style.display = DisplayStyle.None;

            root.Add(topRow);
            root.Add(miningProgressBar);
            root.Add(hotbarLabel);
            root.Add(fadeOverlay);
            documentRoot.Add(root);
        }
    }
}
