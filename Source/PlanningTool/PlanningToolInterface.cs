using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TemplateClasses;
using UnityEngine;

namespace PlanningTool
{
    public class PlanningToolInterface : DragTool
    {
        public static PlanningToolInterface Instance;

        private bool isInitialized;

        public PlanClipboard Clipboard;

        private GameObject _visualizerPlan;
        private GameObject _visualizerClipboard;
        private List<GameObject> _visualizerClipboardObjects = new List<GameObject>();

        public static void DestroyInstance() => Instance = null;

        protected override void OnPrefabInit()
        {
            base.OnPrefabInit();
            Instance = this;

            // populate all fields tagged with [SerializeField] (or public) in DragTool that is probably
            // set through the Unity inspector, using values from DigTool

            // TODO:
            // show grid effect when placing (like when placing building)
            // show the plan to be placed when in tool menu

            FieldInfo areaVisualizerField = AccessTools.Field(typeof(DragTool), "areaVisualizer");

            visualizer = Util.KInstantiate(DigTool.Instance.visualizer);
            // change visualizer to show current selected config
            var mr = visualizer.transform.Find("Mask").GetComponent<MeshRenderer>();
            mr.material = PTAssets.SelectionOutlineMaterial;
            _visualizerPlan = PTObjectTemplates.CreatePlanningTileMesh("PlanPreview", new SaveLoadPlans.PlanData());
            var visualizerMask = _visualizerPlan.transform.Find("Mask");
            var vmPos = visualizerMask.position;
            vmPos.z -= 0.3f;
            visualizerMask.position = vmPos;
            _visualizerPlan.transform.SetParent(visualizer.transform, false);
            _visualizerPlan.SetActive(true);

            _visualizerClipboard = new GameObject("VisualizerClipboard");
            _visualizerClipboard.transform.SetParent(visualizer.transform, false);
            _visualizerClipboard.SetActive(false);

            visualizerLayer = Grid.SceneLayer.SceneMAX;

            var avOriginal = areaVisualizerField.GetValue(DigTool.Instance) as GameObject;
            var av = Util.KInstantiate(avOriginal, gameObject);
            av.SetActive(false);
            areaVisualizerField.SetValue(this, av);
            areaVisualizerSpriteRenderer = av.GetComponent<SpriteRenderer>();
            av.transform.SetParent(transform);
            FieldInfo areaColourField = AccessTools.Field(typeof(DragTool), "areaColour");
            var areaColor = (Color32)areaColourField.GetValue(DigTool.Instance);
            av.GetComponent<Renderer>().material.color = areaColor;

            var boxCursorField = AccessTools.Field(typeof(DragTool), "boxCursor");
            boxCursorField.SetValue(this, boxCursorField.GetValue(DigTool.Instance));
            var areaVisualizerTextPrefabField = AccessTools.Field(typeof(DragTool), "areaVisualizerTextPrefab");
            areaVisualizerTextPrefabField.SetValue(this, areaVisualizerTextPrefabField.GetValue(DigTool.Instance));

            var pthc = gameObject.AddComponent<PlanningToolHoverCard>();
            pthc.ToolName = "Planning";
            pthc.ActionName = "Set plan";
        }

        public void RefreshVisualizerPreview()
        {
            var visualizerPlanPreviewMaskMeshRenderer =
                visualizer.transform.Find("PlanPreview").Find("Mask").GetComponent<MeshRenderer>();
            var activeShape = PlanningToolSettings.Instance.ActiveShape;
            if (activeShape == PlanShape.Rectangle)
                visualizerPlanPreviewMaskMeshRenderer.material = PTAssets.RectangleMaterial;
            else if (activeShape == PlanShape.Circle)
                visualizerPlanPreviewMaskMeshRenderer.material = PTAssets.CircleMaterial;
            else if (activeShape == PlanShape.Diamond)
                visualizerPlanPreviewMaskMeshRenderer.material = PTAssets.DiamondMaterial;
            var col = PlanningToolSettings.Instance.ActiveColor.AsColor();
            col.a = 0.4f;
            visualizerPlanPreviewMaskMeshRenderer.material.color = col;
        }

        public void RefreshClipboardVisualisationPreview()
        {
            if (_visualizerClipboard == null) return;
            foreach (var go in _visualizerClipboardObjects)
            {
                go.SetActive(false);
                Destroy(go);
            }
            _visualizerClipboardObjects.Clear();

            if (!Clipboard.HasData()) return;

            foreach (var element in Clipboard.Elements())
            {
                var cell = Grid.XYToCell(element.OffsetX, element.OffsetY);
                var planData = new SaveLoadPlans.PlanData
                {
                    Cell = cell,
                    Shape = element.Shape,
                    Color = element.Color
                };
                var go = PTObjectTemplates.CreatePlanningTileMesh("ClipboardVisualisationPreview", planData, false);
                var meshRenderer = go.transform.Find("Mask").GetComponent<MeshRenderer>();
                var material = meshRenderer.material;
                var col = material.color;
                col.a = 0.6f;
                material.color = col;
                var pos = Grid.CellToPos(planData.Cell, 0f, 0f, 0f);
                pos.z = -0.1f;
                go.transform.localPosition = pos;
                go.transform.SetParent(_visualizerClipboard.transform, false);
                _visualizerClipboardObjects.Add(go);
                go.SetActive(true);
            }
        }

        protected override void OnDragTool(int cell, int distFromOrigin)
        {
            if (PlanningToolSettings.Instance.PlanningMode == PlanningToolSettings.PlanningToolMode.DragPlan)
                ToolPlacePlan(cell);
            else if (PlanningToolSettings.Instance.PlanningMode == PlanningToolSettings.PlanningToolMode.PlaceClipboard)
            {
                ToolPlaceClipboard(cell);
            }
        }

        private void ToolPlaceClipboard(int cell)
        {
            Grid.CellToXY(cell, out var originX, out var originY);

            foreach (var element in Clipboard.Elements())
            {
                var x = originX + element.OffsetX;
                var y = originY + element.OffsetY;
                var elementCell = Grid.XYToCell(x, y);
                var planData = new SaveLoadPlans.PlanData
                {
                    Cell = elementCell,
                    Color = element.Color,
                    Shape = element.Shape
                };

                PlacePlan(elementCell, planData);
            }
        }

        protected override void OnDragComplete(Vector3 cursorDown, Vector3 cursorUp)
        {
            base.OnDragComplete(cursorDown, cursorUp);

            if (PlanningToolSettings.Instance.PlanningMode == PlanningToolSettings.PlanningToolMode.CopyArea || PlanningToolSettings.Instance.PlanningMode == PlanningToolSettings.PlanningToolMode.CutArea)
            {
                // copy all plans between start and stop to clipboard, use cursorUp as origin
                Grid.PosToXY(cursorUp, out var originX, out var originY);
                var startX = originX;
                var startY = originY;
                Grid.PosToXY(cursorDown, out var endX, out var endY);
                // loop over all values, start top left
                if (startX > endX)
                    Util.Swap(ref startX, ref endX);
                if (startY > endY)
                    Util.Swap(ref startY, ref endY);
                Clipboard.Clear();
                var isCutting = PlanningToolSettings.Instance.PlanningMode ==
                                PlanningToolSettings.PlanningToolMode.CutArea;
                for (int y = startY; y <= endY; y++)
                {
                    for (int x = startX; x <= endX; x++)
                    {
                        var cell = Grid.XYToCell(x, y);
                        if (!SaveLoadPlans.Instance.PlanState.TryGetValue(cell, out var existingData))
                            continue;
                        Clipboard.AddPlan(existingData, originX, originY);
                        if (isCutting)
                        {
                            SaveLoadPlans.Instance.PlanState.Remove(cell);
                            var go = PlanGrid.Plans[cell];
                            PlanGrid.Plans[cell] = null;
                            go.SetActive(false);
                            Destroy(go);
                        }
                    }
                }

                RefreshClipboardVisualisationPreview();

                PlanningToolSettings.Instance.PlanningMode = PlanningToolSettings.PlanningToolMode.PlaceClipboard;
            } else if (PlanningToolSettings.Instance.PlanningMode == PlanningToolSettings.PlanningToolMode.PlaceClipboard)
                PlanningToolSettings.Instance.PlanningMode = PlanningToolSettings.PlanningToolMode.DragPlan;
        }

        private static void ToolPlacePlan(int cell)
        {
            var planData = new SaveLoadPlans.PlanData
            {
                Cell = cell,
                Color = PlanningToolSettings.Instance.ActiveColor,
                Shape = PlanningToolSettings.Instance.ActiveShape
            };

            PlacePlan(cell, planData);
        }

        private static void PlacePlan(int cell, SaveLoadPlans.PlanData planData)
        {
            var cellOccupied = SaveLoadPlans.Instance.PlanState.TryGetValue(cell, out var existingData);
            if (cellOccupied)
            {
                // compare with existing, if it is identical, no need to add gameobject
                if (planData.IsEquivalentTo(existingData))
                {
                    return;
                }
            }

            var go = CreatePlanTile(planData);

            if (cellOccupied)
            {
                var existingGameObject = PlanGrid.Plans[cell];
                existingGameObject.SetActive(false);
                Destroy(existingGameObject);
            }

            SaveLoadPlans.Instance.PlanState[cell] = planData;
            PlanGrid.Plans[cell] = go;
        }

        public static GameObject CreatePlanTile(SaveLoadPlans.PlanData planData)
        {
            var go = PTObjectTemplates.CreatePlanningTileMesh("PlanOverlay", planData);
            var pos = Grid.CellToPosCBC(planData.Cell, Grid.SceneLayer.TileFront);
            pos.z -= 0.1f;
            go.transform.localPosition = pos;
            go.SetActive(true);
            return go;
        }

        protected override void OnActivateTool()
        {
            base.OnActivateTool();
            if (!isInitialized)
            {
                PlanningToolSettings.Instance.OnActiveColorChange += color => RefreshVisualizerPreview();
                PlanningToolSettings.Instance.OnActiveShapeChange += shape => RefreshVisualizerPreview();
                Clipboard = new PlanClipboard();
                PlanningToolSettings.Instance.OnPlanningToolModeChanged += toolMode =>
                {
                    // TODO: add visualizer depending on mode
                    // TODO: remove debug log
                    Debug.Log("Planning mode changed to " + Enum.GetName(typeof(PlanningToolSettings.PlanningToolMode), toolMode));
                    if (toolMode == PlanningToolSettings.PlanningToolMode.PlaceClipboard)
                    {
                        SetMode(Mode.Brush);
                        _visualizerClipboard.SetActive(true);
                    }
                    else
                    {
                        SetMode(Mode.Box);
                        _visualizerClipboard.SetActive(false);
                    }

                    if (toolMode == PlanningToolSettings.PlanningToolMode.DragPlan)
                    {
                        _visualizerPlan.SetActive(true);
                    }
                    else
                    {
                        _visualizerPlan.SetActive(false);
                    }
                };
                isInitialized = true;
            }
        }

        protected override void OnDeactivateTool(InterfaceTool new_tool)
        {
            base.OnDeactivateTool(new_tool);
        }
    }
}
