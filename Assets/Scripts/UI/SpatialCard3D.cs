using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SocraticTutor.UI
{
    public enum StudyReviewRating
    {
        Again = 1,
        Hard = 2,
        Good = 3,
        Easy = 4
    }

    /// <summary>
    /// Interactive World-Space 3D Card for Meta Quest 3 study sessions.
    /// Constructed procedurally at runtime with TextMeshPro and standard Unity UI components.
    /// Implements scale-inversion flip animations (circumventing backface culling artifacts)
    /// and input debouncing to prevent duplicate evaluation submissions.
    /// </summary>
    public sealed class SpatialCard3D : MonoBehaviour
    {
        public const string HostGameObjectName = "SocraticTutor.SpatialCard3D";

        [SerializeField] private float flipDurationSeconds = 0.35f;
        [SerializeField] private float distanceFromCamera = 1.2f;
        [SerializeField] private float targetFovDegrees = 40f;

        private RectTransform _faceContainer;
        private GameObject _frontFace;
        private GameObject _backFace;
        private TextMeshProUGUI _frontText;
        private TextMeshProUGUI _backText;
        private GameObject _showAnswerPanel;
        private GameObject _ratingPanel;

        private bool _isFlipping;
        private bool _acceptingRating;

        public event Action<StudyReviewRating> OnRated;
        public event Action OnExitRequested;

        public void Initialize(Transform eyeAnchor)
        {
            BuildCardInterface();
            PositionInFrontOfUser(eyeAnchor);
        }

        private void BuildCardInterface()
        {
            var canvas = gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            gameObject.AddComponent<CanvasScaler>();
            gameObject.AddComponent<GraphicRaycaster>();

            var canvasRect = GetComponent<RectTransform>();
            var fovRadians = targetFovDegrees * Mathf.Deg2Rad;
            var worldWidth = 2f * distanceFromCamera * Mathf.Tan(fovRadians / 2f);
            var worldHeight = worldWidth * 0.65f;

            canvasRect.sizeDelta = new Vector2(worldWidth, worldHeight) * 1000f;
            transform.localScale = Vector3.one * 0.001f;

            // Background panel
            var background = CreateUiRect(canvasRect, "Background", Vector2.zero, Vector2.one);
            var bgImage = background.gameObject.AddComponent<Image>();
            bgImage.color = new Color(0.12f, 0.14f, 0.18f, 0.95f);

            // Container for card faces
            _faceContainer = CreateUiRect(canvasRect, "FaceContainer", new Vector2(0f, 0.2f), new Vector2(1f, 0.9f));

            // Question face
            var frontRect = CreateUiRect(_faceContainer, "FrontFace", Vector2.zero, Vector2.one);
            _frontFace = frontRect.gameObject;
            _frontText = CreateLabel(frontRect, "Question text...", 28f);

            // Answer face
            var backRect = CreateUiRect(_faceContainer, "BackFace", Vector2.zero, Vector2.one);
            _backFace = backRect.gameObject;
            _backText = CreateLabel(backRect, "Answer text...", 24f);
            _backFace.SetActive(false);

            // Interaction panels
            var controlsRect = CreateUiRect(canvasRect, "ControlsContainer", Vector2.zero, new Vector2(1f, 0.2f));
            BuildControls(controlsRect);
        }

        private void BuildControls(RectTransform container)
        {
            _showAnswerPanel = CreateUiRect(container, "ShowAnswerPanel", Vector2.zero, Vector2.one).gameObject;
            CreateButton(_showAnswerPanel.GetComponent<RectTransform>(), "Show Answer", new Color(0.2f, 0.5f, 0.85f), RevealAnswer);

            _ratingPanel = CreateUiRect(container, "RatingPanel", Vector2.zero, Vector2.one).gameObject;
            var ratingRect = _ratingPanel.GetComponent<RectTransform>();

            CreateButton(ratingRect, "Again", new Color(0.85f, 0.25f, 0.25f), () => SubmitRating(StudyReviewRating.Again), new Vector2(0f, 0f), new Vector2(0.25f, 1f));
            CreateButton(ratingRect, "Hard", new Color(0.85f, 0.55f, 0.2f), () => SubmitRating(StudyReviewRating.Hard), new Vector2(0.25f, 0f), new Vector2(0.5f, 1f));
            CreateButton(ratingRect, "Good", new Color(0.25f, 0.65f, 0.35f), () => SubmitRating(StudyReviewRating.Good), new Vector2(0.5f, 0f), new Vector2(0.75f, 1f));
            CreateButton(ratingRect, "Easy", new Color(0.2f, 0.7f, 0.85f), () => SubmitRating(StudyReviewRating.Easy), new Vector2(0.75f, 0f), new Vector2(1f, 1f));

            _ratingPanel.SetActive(false);
        }

        public void DisplayCard(string question, string answer)
        {
            _frontText.text = question;
            _backText.text = answer;

            _frontFace.SetActive(true);
            _backFace.SetActive(false);
            _showAnswerPanel.SetActive(true);
            _ratingPanel.SetActive(false);

            _faceContainer.localScale = Vector3.one;
            _isFlipping = false;
            _acceptingRating = false;
        }

        public void RevealAnswer()
        {
            if (_isFlipping)
            {
                return;
            }

            StartCoroutine(FlipAnimationRoutine());
        }

        private IEnumerator FlipAnimationRoutine()
        {
            _isFlipping = true;
            var halfDuration = Mathf.Max(0.01f, flipDurationSeconds / 2f);

            yield return AnimateHorizontalScale(1f, 0f, halfDuration);
            _frontFace.SetActive(false);
            _backFace.SetActive(true);
            _showAnswerPanel.SetActive(false);
            _ratingPanel.SetActive(true);
            _acceptingRating = true;

            yield return AnimateHorizontalScale(0f, 1f, halfDuration);
            _isFlipping = false;
        }

        private IEnumerator AnimateHorizontalScale(float startScale, float targetScale, float duration)
        {
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / duration);
                var scale = _faceContainer.localScale;
                scale.x = Mathf.Lerp(startScale, targetScale, t);
                _faceContainer.localScale = scale;
                yield return null;
            }

            var finalScale = _faceContainer.localScale;
            finalScale.x = targetScale;
            _faceContainer.localScale = finalScale;
        }

        private void SubmitRating(StudyReviewRating rating)
        {
            if (!_acceptingRating)
            {
                return;
            }

            _acceptingRating = false;
            OnRated?.Invoke(rating);
        }

        private void PositionInFrontOfUser(Transform headTransform)
        {
            if (headTransform == null)
            {
                return;
            }

            var targetPosition = headTransform.position + headTransform.forward * distanceFromCamera;
            transform.position = targetPosition;
            transform.rotation = Quaternion.LookRotation(transform.position - headTransform.position);
        }

        private static RectTransform CreateUiRect(RectTransform parent, string name, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static TextMeshProUGUI CreateLabel(RectTransform parent, string initialText, float fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(20f, 20f);
            rect.offsetMax = new Vector2(-20f, -20f);

            var text = go.GetComponent<TextMeshProUGUI>();
            text.text = initialText;
            text.fontSize = fontSize;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            return text;
        }

        private static void CreateButton(
            RectTransform parent,
            string label,
            Color color,
            Action onClick,
            Vector2? anchorMin = null,
            Vector2? anchorMax = null)
        {
            var rect = CreateUiRect(parent, $"Btn_{label}", anchorMin ?? Vector2.zero, anchorMax ?? Vector2.one);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;

            var button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => onClick?.Invoke());

            CreateLabel(rect, label, 18f);
        }
    }
}
