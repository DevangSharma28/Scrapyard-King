using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>The ordered main objectives for a stage plus the side-task pool that rotates alongside them.</summary>
    [CreateAssetMenu(fileName = "TaskChain", menuName = "Scrap Yard King/Progression/Task Chain")]
    public sealed class TaskChain : ScriptableObject
    {
        [SerializeField] TaskDefinition[] mainTasks;
        [SerializeField] TaskDefinition[] sideTasks;
        [Tooltip("Main task index after which side tasks start appearing (keeps the tutorial focused).")]
        [SerializeField, Min(0)] int sideTasksFromMainIndex = 6;
        [SerializeField, Min(0)] int maxActiveSideTasks = 1;

        public TaskDefinition[] MainTasks => mainTasks;
        public TaskDefinition[] SideTasks => sideTasks;
        public int SideTasksFromMainIndex => sideTasksFromMainIndex;
        public int MaxActiveSideTasks => maxActiveSideTasks;
    }
}
