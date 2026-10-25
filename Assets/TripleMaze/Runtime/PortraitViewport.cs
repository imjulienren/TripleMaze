using UnityEngine;

namespace TripleMaze
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class PortraitViewport : MonoBehaviour
    {
        public static readonly Vector2 DesignSize = new Vector2(720, 1280);

        void LateUpdate() => Refresh();

        public void Refresh()
        {
            var phone = (RectTransform)transform;
            var parent = (RectTransform)phone.parent;
            if (Screen.width <= 0 || Screen.height <= 0) return;

            // 内容保持等比缩放；背景覆盖全屏，交互区域单独遵守安全区。
            var safe = Screen.safeArea;
            if (safe.width <= 0 || safe.height <= 0)
                safe = new Rect(0, 0, Screen.width, Screen.height);
            var pixelsToCanvas = new Vector2(parent.rect.width / Screen.width, parent.rect.height / Screen.height);
            var available = Vector2.Scale(safe.size, pixelsToCanvas);
            float scale = Mathf.Min(available.x / DesignSize.x, available.y / DesignSize.y);
            phone.anchorMin = phone.anchorMax = new Vector2(.5f, .5f);
            phone.sizeDelta = parent.rect.size / scale;
            phone.anchoredPosition = Vector2.zero;
            phone.localScale = new Vector3(scale, scale, 1);

            // 页面在运行时重建，因此每次刷新都取当前的安全区容器。
            for (int i = 0; i < phone.childCount; i++)
            {
                var page = phone.GetChild(i);
                var content = page.Find("SafeArea") as RectTransform;
                if (!content) continue;
                content.sizeDelta = available / scale;
                content.anchoredPosition = Vector2.Scale(
                    safe.center - new Vector2(Screen.width, Screen.height) * .5f,
                    pixelsToCanvas) / scale;
            }
        }
    }
}
