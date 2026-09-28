namespace PSDL
{
    interface IRoad
    {
        int RowCount { get; }
        int RowBreadth { get; }

        Vertex[] GetRow(int num);
        void AddRow(Vertex[] vertices);
        void SetRow(int row, Vertex[] vertices);
        Vertex GetRowCenterPoint(int row);
#if PSDLLIB_RUNTIME
        void SetTexture(RoadTextureType type, int texture);
        int GetTexture(RoadTextureType type);
#else
        void SetTexture(RoadTextureType type, string texture);
        string GetTexture(RoadTextureType type);
#endif
        Vertex[] GetSidewalkBoundary(int rowNum);
        void DeleteSidewalk(SidewalkRemovalMode mode);
    }
}
