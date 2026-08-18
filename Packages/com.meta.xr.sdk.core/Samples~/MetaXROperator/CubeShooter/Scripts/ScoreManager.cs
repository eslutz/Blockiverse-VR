/*
 * Copyright (c) Meta Platforms, Inc. and affiliates.
 * All rights reserved.
 *
 * Licensed under the Oculus SDK License Agreement (the "License");
 * you may not use the Oculus SDK except in compliance with the License,
 * which is provided at the time of installation or download, or which
 * otherwise accompanies this software in either electronic or hard copy form.
 *
 * You may obtain a copy of the License at
 *
 * https://developer.oculus.com/licenses/oculussdk/
 *
 * Unless required by applicable law or agreed to in writing, the Oculus SDK
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using UnityEngine;

public class ScoreManager : MonoBehaviour
{
    public static ScoreManager Instance { get; private set; }

    public TextMesh scoreText;
    private int score;

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        if (scoreText == null)
        {
            var display = GameObject.Find("ScoreDisplay");
            if (display != null)
                scoreText = display.GetComponent<TextMesh>();
        }

        UpdateDisplay();
    }

    public static void RegisterHit(int points)
    {
        if (Instance == null) return;
        Instance.score += points;
        Debug.Log($"Score changed: {Instance.score} (+{points})");
        Instance.UpdateDisplay();
    }

    void UpdateDisplay()
    {
        if (scoreText != null)
            scoreText.text = "Score: " + score;
    }
}
