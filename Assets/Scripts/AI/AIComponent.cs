namespace MM2.AI
{
    public struct AIComponent
    {
        public CompType Type;
        public int Id;
        public AIComponent(CompType type, int id) { Type = type; Id = id; }
        public bool Matches(AIComponent o) => Type == o.Type && Id == o.Id;
    }
}