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

using Meta.XR.Editor.UserInterface.RLDS;
using UnityEngine.UIElements;
using RLDSStyles = Meta.XR.Editor.UserInterface.RLDS.Styles;
using Label = UnityEngine.UIElements.Label;

namespace Meta.XR.Editor
{
    internal enum CellStyle
    {
        Default,
        Positive,
        Negative,
        Warning,
    }

    /// <summary>
    /// Reusable UIToolkit table component with RLDS styling and data-driven cell coloring.
    /// </summary>
    internal class ComparisonTable
    {
        private readonly string[] _headers;
        private readonly string[][] _rows;
        private readonly float[] _columnWidths;
        private readonly CellStyle[][] _rowStyles;

        public ComparisonTable(string[] headers, string[][] rows, float[] columnWidths,
            CellStyle[][] rowStyles = null)
        {
            _headers = headers;
            _rows = rows;
            _columnWidths = columnWidths;
            _rowStyles = rowStyles;
        }

        public VisualElement Build()
        {
            var container = new VisualElement();
            container.Add(MakeRow(_headers, _columnWidths, isHeader: true));

            for (var i = 0; i < _rows.Length; i++)
            {
                var styles = _rowStyles != null && i < _rowStyles.Length ? _rowStyles[i] : null;
                container.Add(MakeRow(_rows[i], _columnWidths, isHeader: false, cellStyles: styles));
            }

            return container;
        }

        internal static VisualElement MakeRow(string[] cells, float[] columnWidths, bool isHeader,
            CellStyle[] cellStyles = null)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.borderBottomWidth = RLDSConstants.BorderWidth.SizeSM;
            row.style.borderBottomColor = RLDSStyles.Colors.BorderDivider;
            row.style.paddingTop = RLDSConstants.Spacing.Size3XS;
            row.style.paddingBottom = RLDSConstants.Spacing.Size3XS;

            if (isHeader)
            {
                row.style.borderBottomColor = RLDSStyles.Colors.IconSecondary;
                row.style.borderBottomWidth = RLDSConstants.BorderWidth.SizeMD;
            }

            for (var i = 0; i < cells.Length; i++)
            {
                var width = i < columnWidths.Length ? columnWidths[i] : 1f / cells.Length;
                var style = cellStyles != null && i < cellStyles.Length ? cellStyles[i] : CellStyle.Default;
                row.Add(MakeCell(cells[i], isHeader, width, style));
            }

            return row;
        }

        internal static Label MakeCell(string text, bool isHeader, float widthPercent,
            CellStyle style = CellStyle.Default)
        {
            var cell = new Label(text);
            cell.style.width = Length.Percent(widthPercent * 100f);
            cell.style.whiteSpace = WhiteSpace.Normal;
            cell.style.paddingRight = RLDSConstants.Spacing.Size3XS;
            cell.AddToClassList(isHeader
                ? RLDSConstants.Typography.Body2SmallLabel
                : RLDSConstants.Typography.Meta);

            if (!isHeader)
            {
                switch (style)
                {
                    case CellStyle.Positive:
                        cell.style.color = RLDSStyles.Colors.TextPositive;
                        break;
                    case CellStyle.Negative:
                        cell.style.color = RLDSStyles.Colors.TextDisabled;
                        break;
                    case CellStyle.Warning:
                        cell.style.color = RLDSStyles.Colors.TextWarning;
                        break;
                }
            }

            return cell;
        }
    }
}

