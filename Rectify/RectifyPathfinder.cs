﻿using Priority_Queue;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace RectifyUtils
{
    public class PathfinderMetrics
    {
        public int FrontierSize { get; set; }
        public int VisitedNodes { get; set; }
        public long RuntimeInMillis { get; set; }

        public PathfinderMetrics(int frontier, int visited, long runtime)
        {
            FrontierSize = frontier;
            VisitedNodes = visited;
            RuntimeInMillis = runtime;
        }
    }

    public class PathfinderParameters
    {
        public bool DoReachabilityCheck { get; set; } = true;
        public bool UsesLattice { get; set; } = false;
        public bool CondensePaths { get; set; } = true;
    }

    public class RectifyPathfinder
    {
        private PathfinderParameters InstanceParams { get; set; }

        protected class PathQuery
        {
            public readonly RectifyRectangle startRect;
            public readonly RectifyRectangle endRect;
            public List<RectifyRectangle> nearestStartNeighbors = new List<RectifyRectangle>();
            public List<RectifyRectangle> nearestEndNeighbors = new List<RectifyRectangle>();

            /// <summary>
            /// Not used for equality
            /// </summary>
            public List<Position> pathNodes = new List<Position>();

            public readonly HashSet<EdgeType> pathEdges;

            public PathQuery(RectifyRectangle start, RectifyRectangle end, IEnumerable<EdgeType> edges, List<RectifyRectangle> nearestStart, List<RectifyRectangle> nearestEnd, List<Position> path)
            {
                startRect = start;
                endRect = end;
                pathEdges = new HashSet<EdgeType>(edges);
                this.nearestStartNeighbors = nearestStart;
                this.nearestEndNeighbors = nearestEnd;
                this.pathNodes = path;
            }

            //modifying this method based on the MSDN implementation of "TwoDPoint"
            public override bool Equals(object obj)
            {
                // If parameter is null return false.
                if (obj == null)
                {
                    return false;
                }

                // If parameter cannot be cast to Position return false.
                if (!(obj is PathQuery p))
                {
                    return false;
                }

                // Return true if the fields match:
                return (startRect == p.startRect) && (endRect == p.endRect) && (HashSetContainsSameEdges(p.pathEdges)) && ListsOrderedTheSame(this.nearestStartNeighbors, p.nearestStartNeighbors) && ListsOrderedTheSame(this.nearestEndNeighbors, p.nearestEndNeighbors);
            }

            /// <summary>
            /// Returns true if the lists contain the same elements in the same order.
            /// </summary>
            /// <param name="nearestNeighbors1"></param>
            /// <param name="nearestNeighbors2"></param>
            /// <returns></returns>
            private static bool ListsOrderedTheSame(List<RectifyRectangle> myNeighbors, List<RectifyRectangle> othersNeighbors)
            {
                //We only care about neighbors up to the point where we exit.
                //This means our count should always be less-than or equal to the comparing list
                if (myNeighbors.Count > othersNeighbors.Count) return false;
                for (int i = 0; i < myNeighbors.Count; i++)
                {
                    if (myNeighbors[i] != othersNeighbors[i])
                    {
                        return false;
                    }
                }

                return true;
            }

            public bool Equals(PathQuery p)
            {
                // If parameter is null return false:
                if (p is null)
                {
                    return false;
                }

                // Return true if the fields match:
                return (startRect == p.startRect) && (endRect == p.endRect) && (HashSetContainsSameEdges(p.pathEdges)) && ListsOrderedTheSame(this.nearestStartNeighbors, p.nearestStartNeighbors) && ListsOrderedTheSame(this.nearestEndNeighbors, p.nearestEndNeighbors);
            }

            //verifies that the other hashSet contains all of our elements and vice versa
            private bool HashSetContainsSameEdges(HashSet<EdgeType> pathEdges)
            {
                if (this.pathEdges.Count != pathEdges.Count)
                {
                    return false;
                }
                foreach (EdgeType et in pathEdges)
                {
                    if (pathEdges.Contains(et) == false)
                    {
                        return false;
                    }
                }
                return true;
            }

            //copied from MSDN's "TwoDPoint" implementation
            public override int GetHashCode()
            {
                return startRect.GetHashCode() ^ endRect.GetHashCode() + GetPathMask();
            }

            private int GetPathMask()
            {
                int i = 0;
                foreach (EdgeType et in pathEdges)
                {
                    i = i | (int)et;
                }
                return i;
            }

            //not sure we use this outside of limited debugging
            public override string ToString()
            {
                return "Edges: " + GetEdgesString();
            }

            private string GetEdgesString()
            {
                string s = "";
                foreach (EdgeType et in pathEdges)
                {
                    s += et.ToString() + ", ";
                }
                return s.Substring(0, s.Length - 2);
            }
        }

        protected class NodeEdge
        {
            public Position Position { get; set; }
            public Direction Direction { get; set; }

            public NodeEdge(Position p, Direction d)
            {
                this.Position = p;
                this.Direction = d;
            }
        }

        protected class NodeNeighbor
        {
            public RectifyNode Node { get; set; }
            public Direction Direction { get; set; }
            public EdgeType EdgeType { get; set; }

            public NodeNeighbor(RectNeighbor n, Direction d)
            {
                this.EdgeType = n.EdgeType;
                this.Direction = d;
            }
        }

        /// <summary>
        /// Changes the pathgroup at the given Position, splitting one rectangle into 3-5
        /// and sets a dirty flag on the pathfinder.
        /// </summary>
        /// <param name="position"></param>
        /// <param name="pathGroup"></param>
        /// <param name="container"></param>
        /// <returns>A list of the bounds of all rectangles affected by the change</returns>
        public List<RectangleBounds> ReplaceCellAt(Position position, int pathGroup, bool isCenterCell = true, RectifyRectangle container = null)
        {
            //find the rectangle that contains this position.
            var containingRect = container ?? FindRectangleAroundPoint(position);
            var ogPathGroup = containingRect.PathGroup;

            IsDirty = true;

            var offsetVector = position - containingRect.Offset;

            //in all other cases, 3 steps:
            //1. Create a new Rectify Rectangle to be the center cell
            RectifyRectangle centerCell = new RectifyRectangle(position, new Position(position.xPos + 1, position.yPos + 1),
                                                                 pathGroup);
            //2. create top & bottom rectangles (these are the narrow ones, going straight up/down but picked arbitrarily)
            RectifyRectangle topRect = null, botRect = null;
            if (containingRect.Height - offsetVector.yPos - 1 > 0)
            {
                topRect = new RectifyRectangle(new Position(position.xPos, position.yPos + 1), new Position(position.xPos + 1, containingRect.Top),
                                                 ogPathGroup);
            }
            if (offsetVector.yPos > 0)
            {
                botRect = new RectifyRectangle(new Position(position.xPos, containingRect.Bottom), new Position(position.xPos + 1, position.yPos),
                                                 ogPathGroup);
            }
            //3. create left & right rectangles
            RectifyRectangle leftRect = null, rightRect = null;
            if (offsetVector.xPos != 0)
            {
                int leftWidth = offsetVector.xPos - containingRect.Left;
                leftRect = new RectifyRectangle(new Position(containingRect.Left, containingRect.Bottom), new Position(containingRect.Left + offsetVector.xPos, containingRect.Top),
                                                 ogPathGroup);
            }
            if (containingRect.Width - offsetVector.xPos - 1 > 0)
            {
                int rightWidth = containingRect.Right - offsetVector.xPos - 1;
                rightRect = new RectifyRectangle(new Position(position.xPos + 1, containingRect.Bottom), new Position(containingRect.Right, containingRect.Top),
                                                 ogPathGroup);
            }

            List<RectifyRectangle> newRects = new List<RectifyRectangle>() { centerCell };
            //need to copy the parent container's RectNeighbors
            if (topRect != null)
            {
                //left & right edges default to "None", which is what we want, unless we're on a Left / Right edge
                if (leftRect == null)
                {
                    //on left edge, copy from parent
                    int yOffDifference = topRect.Offset.yPos - containingRect.Offset.yPos;
                    for (int i = 0; i < topRect.Height; i++)
                    {
                        topRect.LeftEdge[i].EdgeType = containingRect.LeftEdge[yOffDifference + i].EdgeType;
                    }
                }
                if (rightRect == null)
                {
                    //on right edge, copy from parent
                    int yOffDifference = topRect.Offset.yPos - containingRect.Offset.yPos;
                    for (int i = 0; i < topRect.Height; i++)
                    {
                        topRect.RightEdge[i].EdgeType = containingRect.RightEdge[yOffDifference + i].EdgeType;
                    }
                }

                //top edge is always whatever the containing rect was.
                topRect.TopEdge[0].EdgeType = containingRect.TopEdge[topRect.Offset.xPos - containingRect.Offset.xPos].EdgeType;

                //bottom is always against the center cell, which we calculate below
                newRects.Add(topRect);
            }
            if (botRect != null)
            {
                //left & right edges default to "None", which is what we want, unless we're on a Left / Right edge
                if (leftRect == null)
                {
                    //on left edge, copy from parent
                    for (int i = 0; i < botRect.Height; i++)
                    {
                        botRect.LeftEdge[i].EdgeType = containingRect.LeftEdge[i].EdgeType;
                    }
                }
                if (rightRect == null)
                {
                    //on right edge, copy from parent
                    for (int i = 0; i < botRect.Height; i++)
                    {
                        botRect.RightEdge[i].EdgeType = containingRect.RightEdge[i].EdgeType;
                    }
                }

                //bottom edge is always whatever the containing rect was.
                botRect.BottomEdge[0].EdgeType = containingRect.TopEdge[botRect.Offset.xPos - containingRect.Offset.xPos].EdgeType;
                //top is always against the center cell, which we calculate below
                newRects.Add(botRect);
            }
            if (leftRect != null)
            {
                for (int i = 0; i < leftRect.Width; i++)
                {
                    leftRect.TopEdge[i].EdgeType = containingRect.TopEdge[i].EdgeType;
                    leftRect.BottomEdge[i].EdgeType = containingRect.BottomEdge[i].EdgeType;
                }
                for (int j = 0; j < leftRect.Height; j++)
                {
                    leftRect.LeftEdge[j].EdgeType = containingRect.LeftEdge[j].EdgeType;
                    //right side only borders top & bottom rects (which were split from the same container, and so share the same pathgroup)
                    //OR the center tile (which is calculated further below)
                    leftRect.RightEdge[j].EdgeType = EdgeType.None;
                }
                newRects.Add(leftRect);
            }
            if (rightRect != null)
            {
                for (int i = 0; i < rightRect.Width; i++)
                {
                    //rightRect's Left - containingRect.xOffset == start of array
                    rightRect.TopEdge[i].EdgeType = containingRect.TopEdge[rightRect.Left - containingRect.Offset.xPos + i].EdgeType;
                    rightRect.BottomEdge[i].EdgeType = containingRect.BottomEdge[rightRect.Left - containingRect.Offset.xPos + i].EdgeType;
                }
                for (int j = 0; j < rightRect.Height; j++)
                {
                    rightRect.RightEdge[j].EdgeType = containingRect.RightEdge[j].EdgeType;
                    //left side only borders top & bottom rects (which were split from the same container, and so share the same pathgroup)
                    //OR the center tile (which is calculated further below)
                    rightRect.LeftEdge[j].EdgeType = EdgeType.None;
                }
                newRects.Add(rightRect);
            }

            //now link the rectangles with any neighbors of the parent;
            List<RectifyRectangle> parentNeighbors = containingRect.AllNeighbors;
            parentNeighbors.AddRange(newRects);

            //Link Rectangles here.
            foreach (RectifyRectangle linkRect in parentNeighbors)
            {
                //left edge
                var leftNeighbors = parentNeighbors.FindAll(r => r.Right == linkRect.Left && (linkRect.Bottom < r.Top && linkRect.Top > r.Bottom));
                linkRect.SetNeighbors(leftNeighbors, Direction.West);

                //right edge
                var rightNeighbors = parentNeighbors.FindAll(r => r.Left == linkRect.Right && (linkRect.Bottom < r.Top && linkRect.Top > r.Bottom));
                linkRect.SetNeighbors(rightNeighbors, Direction.East);

                //top edge
                var topNeighbors = parentNeighbors.FindAll(r => r.Bottom == linkRect.Top && (linkRect.Left < r.Right && linkRect.Right > r.Left));
                linkRect.SetNeighbors(topNeighbors, Direction.North);

                //bottom edge
                var bottomNeighbors = parentNeighbors.FindAll(r => r.Top == linkRect.Bottom && (linkRect.Left < r.Right && linkRect.Right > r.Left));
                linkRect.SetNeighbors(bottomNeighbors, Direction.South);
            }

            List<RectangleBounds> outList = new List<RectangleBounds>() { containingRect.ToBounds() };

            //finally, set the edges on the center cell (and surrounding cells) to be wall-type if applicable.
            var tempLeft = leftRect ?? FindRectangleAroundPoint(new Position(position.xPos - 1, position.yPos), true);
            if (tempLeft != null)
            {
                var leftOffsetVector = position - tempLeft.Offset;
                //if pathgroups are the same, use existing edgeType (defaults to "Empty")
                if (tempLeft.PathGroup == centerCell.PathGroup)
                {
                    //if we're not dealing with an edge, this can open up new paths
                    if (isCenterCell)
                    {
                        tempLeft.RightEdge[leftOffsetVector.yPos].EdgeType = EdgeType.None;
                    }
                }
                else
                {
                    tempLeft.RightEdge[leftOffsetVector.yPos].EdgeType = EdgeType.Wall;
                }
                tempLeft.RightEdge[leftOffsetVector.yPos].Neighbor = centerCell;
                centerCell.LeftEdge[0].EdgeType = tempLeft.RightEdge[leftOffsetVector.yPos].EdgeType;

                outList.Add(tempLeft.ToBounds());
            }
            else
            {
                //on left edge
                centerCell.LeftEdge[0].EdgeType = EdgeType.Wall;
            }

            var tempRight = rightRect ?? FindRectangleAroundPoint(new Position(position.xPos + 1, position.yPos), true);
            if (tempRight != null)
            {
                var rightOffsetVector = position - tempRight.Offset;
                //if pathgroups are the same, use existing edgeType (defaults to "Empty")
                if (tempRight.PathGroup == centerCell.PathGroup)
                {
                    //if we're not dealing with an edge, this can open up new paths
                    if (isCenterCell)
                    {
                        tempRight.LeftEdge[rightOffsetVector.yPos].EdgeType = EdgeType.None;
                    }
                }
                else
                {
                    tempRight.LeftEdge[rightOffsetVector.yPos].EdgeType = EdgeType.Wall;
                }
                tempRight.LeftEdge[rightOffsetVector.yPos].Neighbor = centerCell;
                centerCell.RightEdge[0].EdgeType = tempRight.LeftEdge[rightOffsetVector.yPos].EdgeType;

                outList.Add(tempRight.ToBounds());
            }
            else
            {
                //on right edge
                centerCell.RightEdge[0].EdgeType = EdgeType.Wall;
            }

            var tempTop = topRect ?? FindRectangleAroundPoint(new Position(position.xPos, position.yPos + 1), true);
            if (tempTop != null)
            {
                var topOffsetVector = position - tempTop.Offset;
                //if pathgroups are the same, use existing edgeType (defaults to "Empty")
                if (tempTop.PathGroup == centerCell.PathGroup)
                {
                    //if we're not dealing with an edge, this can open up new paths
                    if (isCenterCell)
                    {
                        tempTop.BottomEdge[topOffsetVector.xPos].EdgeType = EdgeType.None;
                    }
                    //do nothing;
                }
                else
                {
                    tempTop.BottomEdge[topOffsetVector.xPos].EdgeType = EdgeType.Wall;
                }
                tempTop.BottomEdge[topOffsetVector.xPos].Neighbor = centerCell;
                centerCell.TopEdge[0].EdgeType = tempTop.BottomEdge[topOffsetVector.xPos].EdgeType;

                outList.Add(tempTop.ToBounds());
            }
            else
            {
                // on top edge
                centerCell.TopEdge[0].EdgeType = EdgeType.Wall;
            }

            var tempBot = botRect ?? FindRectangleAroundPoint(new Position(position.xPos, position.yPos - 1), true);
            if (tempBot != null)
            {
                var botOffsetVector = position - tempBot.Offset;
                //if pathgroups are the same, use existing edgeType (defaults to "Empty")
                if (tempBot.PathGroup == centerCell.PathGroup)
                {
                    //if we're not dealing with an edge, this can open up new paths
                    if (isCenterCell)
                    {
                        tempBot.TopEdge[botOffsetVector.xPos].EdgeType = EdgeType.None;
                    }
                    //do nothing;
                }
                else
                {
                    tempBot.TopEdge[botOffsetVector.xPos].EdgeType = EdgeType.Wall;
                }
                tempBot.TopEdge[botOffsetVector.xPos].Neighbor = centerCell;
                centerCell.BottomEdge[0].EdgeType = tempBot.TopEdge[botOffsetVector.xPos].EdgeType;

                outList.Add(tempBot.ToBounds());
            }
            else
            {
                //on bottom edge
                centerCell.BottomEdge[0].EdgeType = EdgeType.Wall;
            }

            this.RectNodes.Remove(containingRect);
            this.RectNodes.AddRange(newRects);

            return outList;
        }

        /// <summary>
        /// Used for lattice Pathfinders. Use left or bottom cell depending on horiz / vert switch.
        /// Treat as if replacing a cell, then add the new edge to both rects that contain that point;
        /// </summary>
        /// <param name="position"></param>
        /// <param name="edgeDirection"></param>
        /// <param name="pathGroup">the pathgroup of the BASE CELL, not the wall</param>
        public List<RectangleBounds> ReplaceCellEdgeAt(Position position, Direction edgeDirection, EdgeType edge)
        {
            //if nothing changes, early out. But we are basically assuming a Wall will
            //be caught before that, I think.

            //find the rectangle that contains this position.
            var containingRect = FindRectangleAroundPoint(position);

            var modBounds = ReplaceCellAt(position, containingRect.PathGroup, false, containingRect);

            var newRect = FindRectangleAroundPoint(position);
            RectifyRectangle neighborRect = null;

            switch (edgeDirection)
            {
                case Direction.West:
                    newRect.LeftEdge[0].EdgeType = edge;
                    neighborRect = newRect.LeftEdge[0].Neighbor;

                    if (neighborRect != null)
                    {
                        int vertOffset = position.yPos - neighborRect.Offset.yPos;
                        neighborRect.RightEdge[vertOffset].EdgeType = edge;
                    }
                    break;

                case Direction.East:
                    newRect.RightEdge[0].EdgeType = edge;
                    neighborRect = newRect.RightEdge[0].Neighbor;

                    if (neighborRect != null)
                    {
                        int vertOffset = position.yPos - neighborRect.Offset.yPos;
                        neighborRect.LeftEdge[vertOffset].EdgeType = edge;
                    }
                    break;

                case Direction.North:
                    newRect.TopEdge[0].EdgeType = edge;
                    neighborRect = newRect.TopEdge[0].Neighbor;

                    if (neighborRect != null)
                    {
                        int horizOffset = position.xPos - neighborRect.Offset.xPos;
                        neighborRect.BottomEdge[horizOffset].EdgeType = edge;
                    }
                    break;

                case Direction.South:
                    newRect.BottomEdge[0].EdgeType = edge;
                    neighborRect = newRect.BottomEdge[0].Neighbor;

                    if (neighborRect != null)
                    {
                        int horizOffset = position.xPos - neighborRect.Offset.xPos;
                        neighborRect.TopEdge[horizOffset].EdgeType = edge;
                    }
                    break;
            }

            return modBounds;
        }

        private void ReplaceCellAt(Position rectLow, Position rectHigh, int pathGroup)
        {
            throw new NotImplementedException();
        }

        protected class RectifyNode
        {
            public RectifyRectangle NodeRect { get; private set; }

            public int BaseCost
            {
                get
                {
                    return NodeRect.BaseCost;
                }
            }

            public Position Position { get; private set; }

            public RectifyNode(RectifyRectangle nodeRect, Position p)
            {
                this.NodeRect = nodeRect;
                this.Position = p;
            }

            //standard node fields
            public int PathCost { get; set; }

            public RectifyNode PrevNode { get; set; }
            public int Manhatten { get; internal set; }

            //end standard node fields

            //Rectangular Symmetry Reduction helpers
            //public NodeNeighbor Left { get; set; }
            //public NodeNeighbor Right { get; set; }
            //public NodeNeighbor Top { get; set; }
            //public NodeNeighbor Bottom { get; set; }

            public override bool Equals(object obj)
            {
                if (obj is RectifyNode == false) return false;
                return this.NodeRect.Equals((obj as RectifyNode).NodeRect) && this.Position.Equals((obj as RectifyNode).Position);
            }

            public override int GetHashCode()
            {
                return this.NodeRect.GetHashCode() ^ this.Position.GetHashCode();
            }
        }

        private List<RectifyRectangle> RectNodes { get; set; }

        public object NodeCount
        {
            get
            {
                return RectNodes.Count;
            }
        }

        public bool IsDirty { get; private set; }

        public RectifyPathfinder(List<RectifyRectangle> rectNodes, PathfinderParameters pParams = null)
        {
            this.RectNodes = rectNodes;

            SetParameters(pParams ?? new PathfinderParameters());
        }

        private void SetParameters(PathfinderParameters pathfinderParameters)
        {
            this.InstanceParams = pathfinderParameters;
        }

        /// <summary>
        /// Returns the bottomLeft & topRight positions of the rectify rect that encapsulates this point.
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        public Tuple<Position, Position> GetRectBordersFromPoint(Position p)
        {
            //if (IsLattice)
            //{
            //	//multiply starting position
            //	Position truePosition = new Position(p.xPos * 2 + 1, p.yPos * 2 + 1);
            //	int lowX = p.xPos;
            //	int highX = p.xPos;
            //	int lowY = p.yPos;
            //	int highY = p.yPos;

            //	RectifyRectangle startRect = null;

            //	foreach (RectifyRectangle rr in RectNodes)
            //	{
            //		if (rr.ContainsPoint(truePosition, .5f))
            //		{
            //			startRect = rr;
            //			break;
            //		}
            //	}

            //	//look in the 4 cardinal directions until we find something that's not startRect;
            //	RectifyRectangle northLook = FindRectangleAroundPoint(new Position(2 * p.xPos + 1, 2 * (highY + 1) + 1), true);
            //	while (northLook != null)
            //	{
            //		if (northLook == startRect)
            //		{
            //			highY++;
            //			northLook = FindRectangleAroundPoint(new Position(2 * p.xPos + 1, 2 * (highY + 1) + 1), true);
            //		}
            //		else
            //		{
            //			break;
            //		}
            //	}
            //	RectifyRectangle southLook = FindRectangleAroundPoint(new Position(2 * p.xPos + 1, 2 * (lowY - 1) + 1), true);
            //	while (southLook != null)
            //	{
            //		if (southLook == startRect)
            //		{
            //			lowY--;
            //			southLook = FindRectangleAroundPoint(new Position(2 * p.xPos + 1, 2 * (lowY - 1) + 1), true);
            //		}
            //		else
            //		{
            //			break;
            //		}
            //	}

            //	RectifyRectangle westLook = FindRectangleAroundPoint(new Position(2 * (lowX - 1) + 1, 2 * p.yPos + 1), true);
            //	while (westLook != null)
            //	{
            //		if (westLook == startRect)
            //		{
            //			lowX--;
            //			westLook = FindRectangleAroundPoint(new Position(2 * (lowX - 1) + 1, 2 * p.yPos + 1), true);
            //		}
            //		else
            //		{
            //			break;
            //		}
            //	}
            //	RectifyRectangle eastLook = FindRectangleAroundPoint(new Position(2 * (highX + 1) + 1, 2 * p.yPos + 1), true);
            //	while (eastLook != null)
            //	{
            //		if (eastLook == startRect)
            //		{
            //			highX++;
            //			eastLook = FindRectangleAroundPoint(new Position(2 * (highX + 1) + 1, 2 * p.yPos + 1), true);
            //		}
            //		else
            //		{
            //			break;
            //		}
            //	}

            //	return new Tuple<Position, Position>(new Position(lowX, lowY), new Position(highX + 1, highY + 1));

            //}
            //else
            //{
            foreach (RectifyRectangle rr in RectNodes)
            {
                if (rr.ContainsPoint(p, .5f))
                {
                    return new Tuple<Position, Position>(new Position(rr.Left, rr.Bottom), new Position(rr.Right, rr.Top));
                }
            }
            //}

            return null;
        }

        /// <summary>
        /// Calculates a path from the given start position to the given end position for this Pathfinder's list of
        /// rectangles. Only rectangles with a shared edge within flagsMask will be considered. This overload Includes additional
        /// metrics about the nature of the pathfinding.
        /// </summary>
        /// <param name="startPosition"></param>
        /// <param name="endPosition"></param>
        /// <param name="metrics"></param>
        /// <param name="flagsMask"></param>
        /// <returns></returns>
        public List<Position> CalculatePath(Position startPosition, Position endPosition, out PathfinderMetrics metrics, int flagsMask = (int)EdgeType.None)
        {
            var watch = Stopwatch.StartNew();
            PathfinderMetrics results = new PathfinderMetrics(0, 0, 0);
            // something to time
            var pathResult = CalculatePath(startPosition, endPosition, flagsMask, results);
            // done timing
            watch.Stop();
            results.RuntimeInMillis = watch.ElapsedMilliseconds;

            metrics = results;

            return pathResult;
        }

        /// <summary>
        /// Calculates a path from the given start position to the given end position for this Pathfinder's list of
        /// rectangles. Only rectangles with a shared edge within flagsMask will be considered.
        /// </summary>
        /// <param name="startPosition"></param>
        /// <param name="endPosition"></param>
        /// <param name="flagsMask"></param>
        /// <returns></returns>
        public List<Position> CalculatePath(Position startPosition, Position endPosition, int flagsMask = (int)EdgeType.None)
        {
            //hide the metric overload so there's less confusion about which method to call
            return CalculatePath(startPosition, endPosition, flagsMask, null);
        }

        private List<Position> CalculatePath(Position initialPosition, Position finalPosition, int flagsMask = (int)EdgeType.None, PathfinderMetrics metrics = null)
        {
            Position startPosition, endPosition;

            //no path needed.
            if (initialPosition.Equals(finalPosition)) return new List<Position>();

            //Allows for preprocessing (if needed again in the future)
            startPosition = initialPosition;
            endPosition = finalPosition;

            //get valid edgetypes from the mask
            HashSet<EdgeType> edgeTypesFromMask = new HashSet<EdgeType>();
            foreach (EdgeType et in Enum.GetValues(typeof(EdgeType)))
            {
                if (((int)et & flagsMask) == (int)et)
                {
                    edgeTypesFromMask.Add(et);
                }
            }
            //find path rectangles
            RectifyRectangle startRect = FindRectangleAroundPoint(startPosition);
            RectifyRectangle endRect = FindRectangleAroundPoint(endPosition);

            {
                //determine reachability
                if (InstanceParams.DoReachabilityCheck)
                {
                    if (GetRecursiveNeighbors(startRect, endRect, edgeTypesFromMask) == false)
                    {
                        //destination not reachable.
                        return new List<Position>();
                    }
                }

                //calculate the whole path
                List<Position> path = GetPathBetweenRectangles(startPosition, endPosition, startRect, endRect, edgeTypesFromMask, out int visitedNodeCount, out int frontierNodeCount);

                if (path.Count == 0)
                {
                    //no path found
                    return path;
                }

                //add metrics if requested
                if (metrics != null)
                {
                    metrics.FrontierSize = frontierNodeCount;
                    metrics.VisitedNodes = visitedNodeCount;
                }

                return path;
            }
        }

        /// <summary>
        /// If the nearest neighbors for the start / endpoints are in the same order, any path between start / end rect must be the same.
        /// Anything further away than the actual path taken can be discounted as non-optimal. (I think)
        ///
        /// Update from the future: Almost - can't use the neighbor, have to use each possible exit space, but otherwise, correct idea.
        /// Need to update this method to reflect that, but we're also removing caching at the moment, so this is just defunct at present
        /// </summary>
        /// <param name="startRect"></param>
        /// <param name="neighbors"></param>
        /// <param name="initialPath"></param>
        /// <param name="applyReverse"></param>
        private static void TrimNeighbors(RectifyRectangle startRect, List<RectifyRectangle> neighbors, List<Position> initialPath, bool applyReverse)
        {
            var path = new List<Position>(initialPath);
            if (applyReverse) path.Reverse();

            for (int i = 0; i < path.Count; i++)
            {
                if (startRect.ContainsPoint(path[i])) continue;

                //we have the first point not in the startRect
                //minus 1 because if the last rect is the nearest neighbor we need the whole list anyway
                for (int j = 0; j < neighbors.Count - 1; j++)
                {
                    //skip until we find the first path rect
                    if (neighbors[j].ContainsPoint(path[i]) == false) continue;
                    {
                        //drop all neighbors in the j+1th elements.
                        neighbors.RemoveRange(j + 1, neighbors.Count - j - 1);
                    }
                }
                break;
            }
        }

        /// <summary>
        /// Gets a list of the neighbors for the given rect, and the minimum distance to leave
        /// via them.
        /// </summary>
        /// <param name="startRect"></param>
        /// <param name="startPosition"></param>
        /// <returns></returns>
        private List<RectifyRectangle> GetNearestNeighbors(RectifyRectangle startRect, Position startPosition, HashSet<EdgeType> allowedEdges)
        {
            Dictionary<RectifyRectangle, int> nearDistanceCache = new Dictionary<RectifyRectangle, int>();

            //top & bottom edge

            for (int i = 0; i < startRect.TopEdge.Length; i++)
            {
                var topPair = startRect.TopEdge[i];

                if (allowedEdges.Contains(topPair.EdgeType) == false || topPair.Neighbor == null)
                {
                    //not a valid neighbor
                }
                else
                {
                    Position topOffset = startRect.Offset + new Position(i, startRect.Height);

                    if (nearDistanceCache.TryGetValue(topPair.Neighbor, out int oldValue))
                    {
                        int newValue = (topOffset - startPosition).Magnitude;
                        if (newValue < oldValue)
                        {
                            nearDistanceCache[topPair.Neighbor] = newValue;
                        }
                    }
                    else
                    {
                        //not in neighbor cache, add it.
                        nearDistanceCache[topPair.Neighbor] = (topOffset - startPosition).Magnitude;
                    }
                }
                //bottom
                var botPair = startRect.BottomEdge[i];
                if (allowedEdges.Contains(botPair.EdgeType) == false || botPair.Neighbor == null)
                {
                    //not a valid neighbor
                }
                else
                {
                    Position botOffset = startRect.Offset + new Position(i, 0);

                    if (nearDistanceCache.TryGetValue(botPair.Neighbor, out int oldValue))
                    {
                        int newValue = (botOffset - startPosition).Magnitude;
                        if (newValue < oldValue)
                        {
                            nearDistanceCache[botPair.Neighbor] = newValue;
                        }
                    }
                    else
                    {
                        //not in neighbor cache, add it.
                        nearDistanceCache[botPair.Neighbor] = (botOffset - startPosition).Magnitude;
                    }
                }
            }

            //left & right edge

            for (int j = 0; j < startRect.LeftEdge.Length; j++)
            {
                var leftPair = startRect.LeftEdge[j];

                if (allowedEdges.Contains(leftPair.EdgeType) == false || leftPair.Neighbor == null)
                {
                    //not a valid neighbor
                }
                else
                {
                    Position leftOffset = startRect.Offset + new Position(0, j);

                    if (nearDistanceCache.TryGetValue(leftPair.Neighbor, out int oldValue))
                    {
                        int newValue = (leftOffset - startPosition).Magnitude;
                        if (newValue < oldValue)
                        {
                            nearDistanceCache[leftPair.Neighbor] = newValue;
                        }
                    }
                    else
                    {
                        //not in neighbor cache, add it.
                        nearDistanceCache[leftPair.Neighbor] = (leftOffset - startPosition).Magnitude;
                    }
                }
                //bottom
                var rightPair = startRect.RightEdge[j];
                if (allowedEdges.Contains(rightPair.EdgeType) == false || rightPair.Neighbor == null)
                {
                    //not a valid neighbor
                }
                else
                {
                    Position rightOffset = startRect.Offset + new Position(startRect.Width, j);

                    if (nearDistanceCache.TryGetValue(rightPair.Neighbor, out int oldValue))
                    {
                        int newValue = (rightOffset - startPosition).Magnitude;
                        if (newValue < oldValue)
                        {
                            nearDistanceCache[rightPair.Neighbor] = newValue;
                        }
                    }
                    else
                    {
                        //not in neighbor cache, add it.
                        nearDistanceCache[rightPair.Neighbor] = (rightOffset - startPosition).Magnitude;
                    }
                }
            }

            //now convert to list of RectifyRectangles, after orderby-ing the distance.
            var rectList = nearDistanceCache.ToList().OrderBy(kvp => kvp.Value).Select(kvp => kvp.Key);

            return rectList.ToList();
        }

        /// <summary>
        /// For the set of all rectangles, is endRect reachable from startRect?
        /// </summary>
        /// <param name="startRect"></param>
        /// <param name="endRect"></param>
        /// <param name="edgeTypesFromMask"></param>
        /// <returns></returns>
        private bool GetRecursiveNeighbors(RectifyRectangle startRect, RectifyRectangle endRect, HashSet<EdgeType> edgeTypesFromMask)
        {
            //if it's the same rect, always reachable. (Though optimal path may not be, we're not calculating that yet)
            if (startRect == endRect) return true;

            HashSet<RectifyRectangle> foundNeighbors = new HashSet<RectifyRectangle>() { startRect };
            List<RectifyRectangle> neighborsToAdd = new List<RectifyRectangle>(GetNeighborsSimple(startRect, edgeTypesFromMask));

            while (neighborsToAdd.Count > 0)
            {
                var workingNeighbor = neighborsToAdd[0];
                neighborsToAdd.RemoveAt(0);
                foundNeighbors.Add(workingNeighbor);

                if (workingNeighbor == endRect) return true;

                var neighborNeighbors = GetNeighborsSimple(workingNeighbor, edgeTypesFromMask);

                foreach (RectifyRectangle rr in neighborNeighbors)
                {
                    if (rr == endRect) return true;

                    if (foundNeighbors.Contains(rr))
                    {
                        //do nothing, already looked at
                    }
                    else
                    {
                        neighborsToAdd.Add(rr);
                    }
                }
            }

            return false;
        }

        private HashSet<RectifyRectangle> GetNeighborsSimple(RectifyRectangle rect, HashSet<EdgeType> edgeTypesFromMask)
        {
            HashSet<RectifyRectangle> uniqueNeighbors = new HashSet<RectifyRectangle>();

            //left && right
            foreach (var n in rect.LeftEdge)
            {
                if (edgeTypesFromMask.Contains(n.EdgeType))
                {
                    uniqueNeighbors.Add(n.Neighbor);
                }
            }

            foreach (var n in rect.RightEdge)
            {
                if (edgeTypesFromMask.Contains(n.EdgeType))
                {
                    uniqueNeighbors.Add(n.Neighbor);
                }
            }

            foreach (var n in rect.TopEdge)
            {
                if (edgeTypesFromMask.Contains(n.EdgeType))
                {
                    uniqueNeighbors.Add(n.Neighbor);
                }
            }

            foreach (var n in rect.BottomEdge)
            {
                if (edgeTypesFromMask.Contains(n.EdgeType))
                {
                    uniqueNeighbors.Add(n.Neighbor);
                }
            }

            uniqueNeighbors.Remove(null);

            return uniqueNeighbors;
        }

        /// <summary>
        /// Uses a depth-first search to find the optimum path between the two rectangles
        /// </summary>
        /// <param name="startRect"></param>
        /// <param name="endRect"></param>
        /// <param name="edgeTypesFromMask"></param>
        /// <returns></returns>
        private List<Position> GetPathBetweenRectangles(Position startPos, Position endPos, RectifyRectangle startRect, RectifyRectangle endRect, HashSet<EdgeType> edgeTypesFromMask, out int visitedNodeCount, out int frontierNodeCount)
        {
            SimplePriorityQueue<RectifyNode> frontierQueue = new SimplePriorityQueue<RectifyNode>();
            var startNode = new RectifyNode(startRect, startPos) { PathCost = 0 };
            frontierQueue.Enqueue(startNode, 0);

            Dictionary<Position, RectifyNode> visitedNodes = new Dictionary<Position, RectifyNode>();
            Dictionary<Position, RectifyNode> frontierNodes = new Dictionary<Position, RectifyNode>();

            bool foundGoal = false;
            RectifyNode goalNode = null;

            while (frontierQueue.Count > 0)
            {
                RectifyNode currentNode = frontierQueue.Dequeue();
                visitedNodes.Add(currentNode.Position, currentNode);

                //step 0 - check if this is the goal node
                if (currentNode.Position.Equals(endPos))
                {
                    foundGoal = true;
                    goalNode = currentNode;
                    break;
                }

                //step 1 - get all neighbors who match at least one of the edgeTypes allowed
                List<RectifyNode> neighbors = GetValidNeighbors(currentNode, edgeTypesFromMask, endPos, currentNode.NodeRect == endRect);

                //step 2 - determine whether or not to insert neighbors into frontier
                foreach (var node in neighbors)
                {
                    //if it's been visited, ignore it
                    if (visitedNodes.ContainsKey(node.Position)) continue;

                    //if it's in the frontier, either update it, or ignore it
                    if (frontierNodes.ContainsKey(node.Position))
                    {
                        //update the pathCost / previous IFF this pathCostDelta + currentNode's pathCost is lower than previous
                        var ogNode = frontierNodes[node.Position];
                        if (ogNode.PathCost > node.PathCost + currentNode.PathCost)
                        {
                            //this new route is faster
                            ogNode.PathCost = node.PathCost + currentNode.PathCost;
                            ogNode.PrevNode = currentNode;

                            //remove from queue, re-add.
                            frontierQueue.Remove(ogNode);
                            frontierQueue.Enqueue(ogNode, ogNode.PathCost + ogNode.Manhatten);
                        }
                        else
                        {
                            //slower, ignore it
                        }
                    }
                    else
                    {
                        //add this to the frontier, and set the pathCost
                        node.PrevNode = currentNode;
                        node.PathCost = node.PathCost + currentNode.PathCost;
                        frontierNodes.Add(node.Position, node);
                        frontierQueue.Enqueue(node, node.PathCost + node.Manhatten);
                    }
                }

                //step 3 - repeat until we find a goal node
            }

            if (foundGoal == false)
            {
                visitedNodeCount = visitedNodes.Count;
                frontierNodeCount = frontierNodes.Count;
                return new List<Position>();
            }
            else
            {
                List<RectifyNode> reversePath = new List<RectifyNode>();
                RectifyNode iterNode = goalNode;
                while (iterNode != startNode)
                {
                    reversePath.Add(iterNode);
                    iterNode = iterNode.PrevNode;
                }

                //and finally add the start node.
                reversePath.Add(startNode);

                reversePath.Reverse();

                //TODO: Make this optional
                //condense the paths if they're in the same node.
                if (InstanceParams.CondensePaths)
                {
                    var groupedPaths = reversePath.GroupBy(n => n.NodeRect);
                    List<Position> finalPath = new List<Position>();
                    foreach (var rectPos in groupedPaths)
                    {
                        finalPath.Add(rectPos.First().Position);
                        if (rectPos.Count() > 1)
                        {
                            finalPath.Add(rectPos.Last().Position);
                        }
                    }
                    visitedNodeCount = visitedNodes.Count;
                    frontierNodeCount = frontierNodes.Count;

                    return finalPath;
                }
                else
                {
                    visitedNodeCount = visitedNodes.Count;
                    frontierNodeCount = frontierNodes.Count;

                    return reversePath.Select(s => s.Position).ToList();
                }
            }
        }

        /// <summary>
        /// Gets all neighbors of the given RectifyRectangle which share an edge with one of the valid
        /// edge types from the mask. If the neighbor would be in the same rect, "jump" to the far side of the rect and return that jump instead.
        /// </summary>
        /// <param name="nodeRect"></param>
        /// <param name="edgeTypesFromMask"></param>
        /// <returns></returns>
        private List<RectifyNode> GetValidNeighbors(RectifyNode currentNode, HashSet<EdgeType> edgeTypesFromMask, Position goalPos, bool isGoalRect)
        {
            List<RectifyNode> outNodes = new List<RectifyNode>();

            Position nodePos = currentNode.Position;
            RectifyRectangle parent = currentNode.NodeRect;

            //if we're in the goal Rect, add the goal as a neighbor
            if (isGoalRect)
            {
                RectifyNode goalNode = new RectifyNode(parent, new Position(goalPos));
                //                     base cost * distance travelled                         + cost to get here
                goalNode.PathCost = parent.BaseCost * (nodePos.xPos - goalNode.Position.xPos);
                outNodes.Add(goalNode);
            }

            //Add Diagonals in here. Even if you don't allow diagnoal movement, we can take advantage of
            //it here to find the ideal node paths b/c we're in a rectangle.

            //topLeft
            {
                RectifyNode topLeft = null;
                //can't be on the left or top edges already.
                if (parent.Left < nodePos.xPos && parent.Top - 1 > nodePos.yPos)
                {
                    //add -1,+1 until nodePos.xpos = left or parent.Top - 1
                    var leftSteps = nodePos.xPos - parent.Left;
                    var upSteps = parent.Top - 1 - nodePos.yPos;
                    var minSteps = Math.Min(leftSteps, upSteps);

                    topLeft = new RectifyNode(parent, new Position(nodePos.xPos - minSteps, nodePos.yPos + minSteps));
                    //                     base cost * distance travelled                         + cost to get here
                    topLeft.PathCost = parent.BaseCost * ((nodePos - topLeft.Position).Magnitude);
                    topLeft.Manhatten = (goalPos - topLeft.Position).Magnitude;
                    outNodes.Add(topLeft);
                }
            }

            //topRight
            {
                RectifyNode topRight = null;
                //can't be on the right or top edges already.
                if (parent.Right - 1 > nodePos.xPos && parent.Top - 1 > nodePos.yPos)
                {
                    //add +1,+1 until nodePos.xpos = right - 1 or parent.Top - 1
                    var rightSteps = parent.Right - 1 - nodePos.xPos;
                    var upSteps = parent.Top - 1 - nodePos.yPos;
                    var minSteps = Math.Min(rightSteps, upSteps);

                    topRight = new RectifyNode(parent, new Position(nodePos.xPos + minSteps, nodePos.yPos + minSteps));
                    //                     base cost * distance travelled                         + cost to get here
                    topRight.PathCost = parent.BaseCost * ((nodePos - topRight.Position).Magnitude);
                    topRight.Manhatten = (goalPos - topRight.Position).Magnitude;
                    outNodes.Add(topRight);
                }
            }

            //bottomRight
            {
                RectifyNode bottomRight = null;
                //can't be on the right or bottom edges already.
                if (parent.Right - 1 > nodePos.xPos && parent.Bottom < nodePos.yPos)
                {
                    //add +1,-1 until nodePos.xpos = right -1 or parent.Bottom
                    var rightSteps = parent.Right - 1 - nodePos.xPos;
                    var downSteps = nodePos.yPos - parent.Bottom;
                    var minSteps = Math.Min(downSteps, rightSteps);

                    bottomRight = new RectifyNode(parent, new Position(nodePos.xPos + minSteps, nodePos.yPos - minSteps));
                    //                     base cost * distance travelled                         + cost to get here
                    bottomRight.PathCost = parent.BaseCost * ((nodePos - bottomRight.Position).Magnitude);
                    bottomRight.Manhatten = (goalPos - bottomRight.Position).Magnitude;
                    outNodes.Add(bottomRight);
                }
            }

            //bottomLeft
            {
                RectifyNode bottomLeft = null;
                //can't be on the right or bottom edges already.
                if (parent.Left < nodePos.xPos && parent.Bottom < nodePos.yPos)
                {
                    //add -1,-1 until nodePos.xpos = parent.left or parent.Bottom
                    var leftSteps = nodePos.xPos - parent.Left;
                    var downSteps = nodePos.yPos - parent.Bottom;
                    var minSteps = Math.Min(downSteps, leftSteps);

                    bottomLeft = new RectifyNode(parent, new Position(nodePos.xPos - minSteps, nodePos.yPos - minSteps));
                    //                     base cost * distance travelled                         + cost to get here
                    bottomLeft.PathCost = parent.BaseCost * ((nodePos - bottomLeft.Position).Magnitude);
                    bottomLeft.Manhatten = (goalPos - bottomLeft.Position).Magnitude;
                    outNodes.Add(bottomLeft);
                }
            }

            //left
            {
                RectifyNode leftNode = null;
                if (parent.Left < nodePos.xPos && (parent.Top - 1 == nodePos.yPos || parent.Bottom == nodePos.yPos))
                {
                    //return the adjacent node within this macro edge
                    //JPS implementation goes here, I think
                    leftNode = new RectifyNode(parent, new Position(nodePos.xPos - 1, nodePos.yPos));
                }
                else if (parent.Left < nodePos.xPos)
                {
                    //"jump" and return corresponding left-most node within this rect.
                    leftNode = new RectifyNode(parent, new Position(parent.Left, nodePos.yPos));
                }
                else if (parent.Left == nodePos.xPos)
                {
                    //look in the leftEdge box to see if there's a neighbor.
                    var seeker = parent.LeftEdge[nodePos.yPos - parent.Offset.yPos];
                    if (seeker.Neighbor != null && edgeTypesFromMask.Contains(seeker.EdgeType))
                    {
                        //make new valid node
                        leftNode = new RectifyNode(seeker.Neighbor, new Position(nodePos.xPos - 1, nodePos.yPos));
                    }
                }
                if (leftNode != null)
                {
                    //                     base cost * distance travelled                         + cost to get here
                    leftNode.PathCost = parent.BaseCost * (nodePos.xPos - leftNode.Position.xPos);
                    leftNode.Manhatten = (goalPos - leftNode.Position).Magnitude;
                    outNodes.Add(leftNode);
                }
            }
            //right
            {
                RectifyNode rightNode = null;
                if (parent.Right - 1 > nodePos.xPos && (parent.Top - 1 == nodePos.yPos || parent.Bottom == nodePos.yPos))
                {
                    //return the adjacent node within this macro edge
                    //JPS implementation goes here, I think
                    rightNode = new RectifyNode(parent, new Position(nodePos.xPos + 1, nodePos.yPos));
                }
                else if (parent.Right - 1 > nodePos.xPos)
                {
                    //"jump" and return corresponding left-most node within this rect.
                    rightNode = new RectifyNode(parent, new Position(parent.Right - 1, nodePos.yPos));
                }
                else if (parent.Right - 1 == nodePos.xPos)
                {
                    //look in the rightEdge box to see if there's a neighbor.
                    var seeker = parent.RightEdge[nodePos.yPos - parent.Offset.yPos];
                    if (seeker.Neighbor != null && edgeTypesFromMask.Contains(seeker.EdgeType))
                    {
                        //make new valid node
                        rightNode = new RectifyNode(seeker.Neighbor, new Position(nodePos.xPos + 1, nodePos.yPos));
                    }
                }
                if (rightNode != null)
                {
                    //                       base cost * distance travelled                         + cost to get here
                    rightNode.PathCost = parent.BaseCost * (rightNode.Position.xPos - nodePos.xPos);
                    rightNode.Manhatten = (goalPos - rightNode.Position).Magnitude;
                    outNodes.Add(rightNode);
                }
            }

            //top
            {
                RectifyNode topNode = null;
                if (parent.Top - 1 > nodePos.yPos && (parent.Left == nodePos.xPos || parent.Right - 1 == nodePos.xPos))
                {
                    //return the adjacent node within this macro edge
                    //JPS implementation goes here, I think
                    topNode = new RectifyNode(parent, new Position(nodePos.xPos, nodePos.yPos + 1));
                }
                else if (parent.Top - 1 > nodePos.yPos)
                {
                    //"jump" and return corresponding left-most node within this rect.
                    topNode = new RectifyNode(parent, new Position(nodePos.xPos, parent.Top - 1));
                }
                else if (parent.Top - 1 == nodePos.yPos)
                {
                    //look in the topEdge box to see if there's a neighbor.
                    var seeker = parent.TopEdge[nodePos.xPos - parent.Offset.xPos];
                    if (seeker.Neighbor != null && edgeTypesFromMask.Contains(seeker.EdgeType))
                    {
                        //make new valid node
                        topNode = new RectifyNode(seeker.Neighbor, new Position(nodePos.xPos, nodePos.yPos + 1));
                    }
                }
                if (topNode != null)
                {
                    //                   base cost * distance travelled                         + cost to get here
                    topNode.PathCost = parent.BaseCost * (topNode.Position.yPos - nodePos.yPos);
                    topNode.Manhatten = (goalPos - topNode.Position).Magnitude;
                    outNodes.Add(topNode);
                }
            }

            //bottom
            {
                RectifyNode bottomNode = null;
                if (parent.Bottom < nodePos.yPos && (parent.Left == nodePos.xPos || parent.Right - 1 == nodePos.xPos))
                {
                    //return the adjacent node within this macro edge
                    //JPS implementation goes here, I think
                    bottomNode = new RectifyNode(parent, new Position(nodePos.xPos, nodePos.yPos - 1));
                }
                else if (parent.Bottom < nodePos.yPos)
                {
                    //"jump" and return corresponding left-most node within this rect.
                    bottomNode = new RectifyNode(parent, new Position(nodePos.xPos, parent.Bottom));
                }
                else if (parent.Bottom == nodePos.yPos)
                {
                    //look in the bottomEdge box to see if there's a neighbor.
                    var seeker = parent.BottomEdge[nodePos.xPos - parent.Offset.xPos];
                    if (seeker.Neighbor != null && edgeTypesFromMask.Contains(seeker.EdgeType))
                    {
                        //make new valid node
                        bottomNode = new RectifyNode(seeker.Neighbor, new Position(nodePos.xPos, nodePos.yPos - 1));
                    }
                }
                if (bottomNode != null)
                {
                    //                   base cost * distance travelled                               + cost to get here
                    bottomNode.PathCost = parent.BaseCost * (nodePos.yPos - bottomNode.Position.yPos);
                    bottomNode.Manhatten = (goalPos - bottomNode.Position).Magnitude;
                    outNodes.Add(bottomNode);
                }
            }

            return outNodes;
        }

        ///// <summary>
        ///// Gets all neighbors of the given RectifyRectangle which share an edge with one of the valid
        ///// edge types from the mask
        ///// </summary>
        ///// <param name="nodeRect"></param>
        ///// <param name="edgeTypesFromMask"></param>
        ///// <returns></returns>
        //private List<RectifyNode> GetValidNeighbors(RectifyRectangle nodeRect, NodeEdge startPos, HashSet<EdgeType> edgeTypesFromMask)
        //{
        //	Dictionary<RectifyRectangle, List<NodeEdge>> neighborDict = new Dictionary<RectifyRectangle, List<NodeEdge>>();

        //	//left && right
        //	for (int i = 0; i < nodeRect.Height; i++)
        //	{
        //		RectNeighbor left = nodeRect.LeftEdge[i];

        //		if (left.Neighbor != null && edgeTypesFromMask.Contains(left.EdgeType))
        //		{
        //			if (neighborDict.ContainsKey(left.Neighbor))
        //			{
        //				//add this position to the neighbor's position list
        //				neighborDict[left.Neighbor].Add(new NodeEdge(new Position(nodeRect.Left, nodeRect.Bottom + i), Direction.West));
        //			}
        //			else
        //			{
        //				//add this neighbor to the list
        //				List<NodeEdge> firstPosition = new List<NodeEdge>() { new NodeEdge(new Position(nodeRect.Left, nodeRect.Bottom + i), Direction.West) };
        //				neighborDict[left.Neighbor] = firstPosition;
        //			}
        //		}

        //		RectNeighbor right = nodeRect.RightEdge[i];

        //		if (right.Neighbor != null && edgeTypesFromMask.Contains(right.EdgeType))
        //		{
        //			if (neighborDict.ContainsKey(right.Neighbor))
        //			{
        //				//add this position to the neighbor's position list
        //				neighborDict[right.Neighbor].Add(new NodeEdge(new Position(nodeRect.Right, nodeRect.Bottom + i), Direction.East));
        //			}
        //			else
        //			{
        //				//add this neighbor to the list
        //				List<NodeEdge> firstPosition = new List<NodeEdge>() { new NodeEdge(new Position(nodeRect.Right, nodeRect.Bottom + i), Direction.East) };
        //				neighborDict[right.Neighbor] = firstPosition;
        //			}
        //		}
        //	}
        //	//top & bot
        //	for (int i = 0; i < nodeRect.Width; i++)
        //	{
        //		RectNeighbor top = nodeRect.TopEdge[i];

        //		if (top.Neighbor != null && edgeTypesFromMask.Contains(top.EdgeType))
        //		{
        //			if (neighborDict.ContainsKey(top.Neighbor))
        //			{
        //				//add this position to the neighbor's position list
        //				neighborDict[top.Neighbor].Add(new NodeEdge(new Position(nodeRect.Left + i, nodeRect.Top), Direction.North));
        //			}
        //			else
        //			{
        //				//add this neighbor to the list
        //				List<NodeEdge> firstPosition = new List<NodeEdge>() { new NodeEdge(new Position(nodeRect.Left + i, nodeRect.Top), Direction.North) };
        //				neighborDict[top.Neighbor] = firstPosition;
        //			}
        //		}

        //		RectNeighbor bottom = nodeRect.BottomEdge[i];

        //		if (bottom.Neighbor != null && edgeTypesFromMask.Contains(bottom.EdgeType))
        //		{
        //			if (neighborDict.ContainsKey(bottom.Neighbor))
        //			{
        //				//add this position to the neighbor's position list
        //				neighborDict[bottom.Neighbor].Add(new NodeEdge(new Position(nodeRect.Left + i, nodeRect.Bottom), Direction.South));
        //			}
        //			else
        //			{
        //				//add this neighbor to the list
        //				List<NodeEdge> firstPosition = new List<NodeEdge>() { new NodeEdge(new Position(nodeRect.Left + i, nodeRect.Bottom), Direction.South) };
        //				neighborDict[bottom.Neighbor] = firstPosition;
        //			}
        //		}
        //	}

        //	List<RectifyNode> outList = new List<RectifyNode>();

        //	//have all of the neighbors, now turn them into nodes
        //	foreach (var neighborPair in neighborDict)
        //	{
        //		//find position w/ shortest magnitude from startPosition

        //		var shortestEntrance = neighborPair.Value.OrderBy(p => (p.Position - startPos.Position).Magnitude).First();

        //		outList.Add(new RectifyNode(neighborPair.Key)
        //		{
        //			EntryPoint = shortestEntrance
        //		});
        //	}

        //	return outList;
        //}

        private RectifyRectangle FindRectangleAroundPoint(Position position, bool allowNull = false)
        {
            foreach (RectifyRectangle rr in RectNodes)
            {
                if (rr.ContainsPoint(position, .5f)) return rr;
            }

            if (allowNull) return null;

            throw new PathOutOfBoundsException("Position: " + position.ToString() + "was not within this pathfinder's rect nodes");
        }
    }
}