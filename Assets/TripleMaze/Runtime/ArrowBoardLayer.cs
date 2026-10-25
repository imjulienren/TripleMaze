using UnityEngine;
using UnityEngine.UI;

namespace TripleMaze
{
    // Each layer shares the board's model, paths, animation state and coordinate system.
    // Hit testing stays on the parent board so splitting the mesh does not split input.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ArrowBoardLayer : MaskableGraphic
    {
        public ArrowBoardGraphic Board;
        public int StartIndex;
        protected override void OnPopulateMesh(VertexHelper vh)
        {if(Board)Board.PopulateRange(vh,StartIndex,32);else vh.Clear();}
    }
}
