using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace Gamebook
{
   public class Graphml
   {
      private readonly XNamespace g = "http://graphml.graphdrawing.org/xmlns";
      private readonly XNamespace y = "http://www.yworks.com/xml/graphml";
      private XElement Root;

      public Graphml(
         string source)
      {
         Root = XElement.Parse(source);
      }

      public IEnumerable<(string nodeId, string label)> Nodes()
      {
         return
            from node in Root.Descendants(g + "node")
            where node.Attribute("yfiles.foldertype")?.Value != "group"
            select (
               node.Attribute("id").Value,
               node.Descendants(y + "NodeLabel").First().Value);
      }

      public IEnumerable<(string edgeId, string sourceNode, string targetNode, string label)> Edges()
      {
         return
            from edge in Root.Descendants(g + "edge")
            select (
               edge.Attribute("id").Value,
               edge.Attribute("source").Value,
               edge.Attribute("target").Value,
               edge.Descendants(y + "EdgeLabel").DefaultIfEmpty(null)?.First()?.Value);
      }
   }
}
