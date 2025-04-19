/*
 
MIT License

Copyright (c) 2020 Kitbashery

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.


Created by Jorge Reyna Tamez @ https://www.fiverr.com/wkepro
Created for Kitbashery @ https://www.kitbashery.com

*/


using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Kitbashery.MeshCombiner
{
    public static class UVPacker
    {
        internal class Node
        {
            public Node Child1;
            public Node Child2;
            public Node Child3;
            public float X;
            public float Y;
            public float Width;
            public float Height;
            public bool Used;
        }

        public class Box
        {
            public float X;
            public float Y;
            public float Width;
            public float Height;
            public float ShiftX;
            public float ShiftY;
            public float Side;
            public MeshExtended Extended;
            public Vector2[] PackedUVs;
        }


        static Box UVBox(MeshExtended extended, float gap)
        {

            // find the minimum and maximum values for X and Y.
            Vector2[] uvs = extended.UVs;
            float xMin = Mathf.Infinity;
            float xMax = -Mathf.Infinity;
            float yMin = Mathf.Infinity;
            float yMax = -Mathf.Infinity;

            foreach (Vector2 v2 in uvs)
            {
                if (v2.x < xMin)
                {
                    xMin = v2.x;
                }
                if (v2.x > xMax)
                {
                    xMax = v2.x;
                }
                if (v2.y < yMin)
                {
                    yMin = v2.y;
                }
                if (v2.y > yMax)
                {
                    yMax = v2.y;
                }
            }

            // calculate the with and height plus a small gap definded by the user.
            float wid = xMax - xMin + gap;
            float hgt = yMax - yMin + gap;

            // calculate the box size base on the 3D size.
            Vector3 size = extended.Size;
            float sid = size.x * size.y * size.z;

            return new Box { Height = hgt, Width = wid, Side = sid, Extended = extended, ShiftX = xMin, ShiftY = yMin };

        }

        static Node FindNode(Node node, float width, float height)
        {

            // Find an empty node with enough space to fit the box.
            if (node.Used)
            {
                Node next = node.Child1 == null ? null : FindNode(node.Child1, width, height);
                if (next != null)
                {
                    return next;
                }
                next = node.Child2 == null ? null : FindNode(node.Child2, width, height);
                if (next != null)
                {
                    return next;
                }
                return node.Child3 == null ? null : FindNode(node.Child3, width, height);
            }
            if (width <= node.Width && height <= node.Height)
            {
                return node;
            }
            return null;

        }

        static void SplitNode(Node node, Box box)
        {

            float x = node.X;
            float y = node.Y;
            box.X = x;
            box.Y = y;

            // calculate the space left to the right and upwards.
            float dw = node.Width - box.Side;
            float dl = node.Height - box.Side;

            // adjust the node size to the box size.
            node.Used = true;
            node.Width = box.Side;
            node.Height = box.Side;
            if (dw > 0)
            {
                // add a node to the right is there's unused horizontal space.
                node.Child1 = new Node { X = x + node.Width, Y = y, Width = dw, Height = node.Height };
            }
            if (dl > 0)
            {
                // add a node above if there's unused vertical space.
                node.Child2 = new Node { X = x, Y = y + node.Height, Width = node.Width + dw, Height = dl };
            }

        }

        static Node AttachNode(Node root, Box box)
        {

            // Mo empty space was found, so create some.

            Node used, empty, parent;
            float x, y;
            // chose where to add the new space, in such a way that over time the resulting container becomes closer to a square.
            if (root.Width > root.Height)
            {
                // calulate the space that wil be left unused if the node is added to the right.
                float dw = Mathf.Abs(root.Width - box.Side);
                x = 0;
                y = root.Height;
                // Create a node for the empty space.
                if (root.Width < box.Side)
                {
                    empty = new Node { X = root.Width, Y = 0, Width = dw, Height = root.Height };
                }
                else
                {
                    empty = new Node { X = box.Side, Y = y, Width = dw, Height = box.Side };
                }
                // Create a node for the box.
                used = new Node { X = 0, Y = y, Width = box.Side, Height = box.Side, Used = true };
                // and add a new parent.
                parent = new Node { X = 0, Y = 0, Width = root.Width, Height = root.Height + box.Side, Used = true, Child1 = root, Child2 = used, Child3 = empty };
            }
            else
            {
                // calulate the space that wil be left unused if the node is added above.
                float dl = Mathf.Abs(root.Height - box.Side);
                x = root.Width;
                y = 0;
                // Create a node for the empty space.
                if (root.Height < box.Side)
                {
                    empty = new Node { X = 0, Y = root.Height, Width = root.Width, Height = dl };
                }
                else
                {
                    empty = new Node { X = x, Y = box.Side, Width = box.Side, Height = dl };
                }
                // Create a node for the box.
                used = new Node { X = x, Y = 0, Width = box.Side, Height = box.Side, Used = true };
                // and add a new parent.
                parent = new Node { X = 0, Y = 0, Width = root.Width + box.Side, Height = root.Height, Used = true, Child1 = root, Child2 = used, Child3 = empty };
            }
            box.X = x;
            box.Y = y;
            return parent;

        }

        static void AdjustUVs(Box box)
        {

            // make sure the UV fit in the 0-1 space (inside the box)
            Vector2[] uv = box.Extended.UVs;
            int count = uv.Length;
            float sqx = box.Side / box.Width;
            float sqy = box.Side / box.Height;
            float x = box.X;
            float y = box.Y;
            float shiftx = box.ShiftX;
            float shifty = box.ShiftY;

            Vector2[] packed = new Vector2[count];
            for (int u = 0; u < count; u++)
            {
                packed[u].x = x + (uv[u].x - shiftx) * sqx;
                packed[u].y = y + (uv[u].y - shifty) * sqy;
            }
            box.PackedUVs = packed;

        }

        static public List<Box> Pack(MeshExtended[] extended, float gap)
        {

            int meshes = extended.Length;
            List<Box> boxes = new List<Box>();

            for (int m = 0; m < meshes; m++)
            {
                boxes.Add(UVBox(extended[m], gap));
            }

            if (meshes < 3)
            {

                // if 1 & 2 meshes, there is no need to scale.
                for (int m = 0; m < meshes; m++)
                {
                    boxes[m].Side = 1;
                }

            }
            else
            {

                // get the maximum box size;
                float maxSize = -Mathf.Infinity;
                for (int m = 0; m < meshes; m++)
                {
                    float sz = boxes[m].Side;
                    if (sz > maxSize)
                    {
                        maxSize = sz;
                    }
                }

                // standarize scaling
                float scale1 = maxSize / 32;
                float scale2 = maxSize / 16;
                float scale3 = maxSize / 8;
                float scale4 = maxSize / 4;
                float scale5 = maxSize / 2;
                float totSize = 0;

                // adjust scale to avoid huge steps
                for (int m = 0; m < meshes; m++)
                {
                    float scaled = boxes[m].Side;
                    if (scaled <= scale1)
                    {
                        boxes[m].Side = 1;
                    }
                    else if (scaled <= scale2)
                    {
                        boxes[m].Side = 2;
                    }
                    else if (scaled <= scale3)
                    {
                        boxes[m].Side = 3;
                    }
                    else if (scaled <= scale4)
                    {
                        boxes[m].Side = 4;
                    }
                    else if (scaled <= scale5)
                    {
                        boxes[m].Side = 5;
                    }
                    else
                    {
                        boxes[m].Side = 6;
                    }
                    totSize += boxes[m].Side;
                }

                boxes = boxes.OrderByDescending(box => box.Side).ToList();

                // the cases can be easily optimized to fit 100% of the space.
                switch (meshes)
                {
                    case 3:
                        boxes[0].Side = 2;
                        boxes[1].Side = 1;
                        boxes[2].Side = 1;
                        break;

                    case 4:
                        if (boxes[0].Side == boxes[1].Side)
                        {
                            for (int m = 0; m < meshes; m++)
                            {
                                boxes[m].Side = 1;
                            }
                        }
                        else
                        {
                            boxes[0].Side = 3;
                            boxes[1].Side = 1;
                            boxes[2].Side = 1;
                            boxes[3].Side = 1;
                        }
                        break;

                    case 5:
                        if (boxes[0].Side == boxes[2].Side)
                        {
                            boxes[0].Side = 3;
                            boxes[1].Side = 3;
                            boxes[2].Side = 2;
                            boxes[3].Side = 2;
                            boxes[4].Side = 2;
                        }
                        else
                        {
                            boxes[0].Side = 4;
                            boxes[1].Side = 1;
                            boxes[2].Side = 1;
                            boxes[3].Side = 1;
                            boxes[4].Side = 1;
                        }
                        break;

                }

            }

            // make a root node based on the first (biggest) box.
            Node root = new Node { Height = boxes[0].Side, Width = boxes[0].Side };
            foreach (Box box in boxes)
            {
                Node node = FindNode(root, box.Side, box.Side);
                if (node == null)
                {
                    // if there's not enough space then add extra space.
                    root = AttachNode(root, box);
                }
                else
                {
                    // split the node to take only the necesary space.
                    SplitNode(node, box);
                }
            }

            // once we have all the boxes at their correspinding positions and scales, let's ajust the UVs to fit each box.
            foreach (Box box in boxes)
            {
                AdjustUVs(box);
            }
            return boxes;

        }

        /// <summary>
        /// Corrects the UV scale to fit in the 0-1 space (global).
        /// </summary>
        /// <param name="uv"></param>
        public static void AdjustSpace(List<Vector2> uv)
        {
            int count = uv.Count;
            float xMax = -Mathf.Infinity;
            float yMax = -Mathf.Infinity;

            foreach (Vector2 v2 in uv)
            {
                if (v2.x > xMax)
                {
                    xMax = v2.x;
                }
                if (v2.y > yMax)
                {
                    yMax = v2.y;
                }
            }
            float max = xMax > yMax ? xMax : yMax;
            if (max > 0)
            {
                for (int u = 0; u < count; u++)
                {
                    uv[u] /= max;
                }
            }
        }
    }
}