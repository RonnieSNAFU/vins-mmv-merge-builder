using HKLib.hk2018;
using NRMerge;
using Xunit;

public class BehaviorMergeTests
{
    static hkbClipGenerator Clip(string name, float speed = 1) => new() { m_name = name, m_animationName = name, m_playbackSpeed = speed };

    static CustomManualSelectorGenerator Cmsg(string name, params hkbGenerator[] gens)
    {
        var c = new CustomManualSelectorGenerator { m_name = name };
        c.m_generators.AddRange(gens);
        return c;
    }

    static hkbStateMachine.StateInfo State(string name, int id, hkbGenerator g) => new() { m_name = name, m_stateId = id, m_generator = g };

    /// <summary>Base graph: SM with states Idle (CMSG Idle_CMSG → a000_000000) and Attack (CMSG Attack_CMSG → a000_003000).</summary>
    static hkRootLevelContainer Graph()
    {
        var sm = new hkbStateMachine { m_name = "Root_SM" };
        sm.m_states.Add(State("Idle", 0, Cmsg("Idle_CMSG", Clip("a000_000000"))));
        sm.m_states.Add(State("Attack", 1, Cmsg("Attack_CMSG", Clip("a000_003000"))));
        var g = new hkbBehaviorGraph { m_name = "c0000.hkb", m_rootGenerator = sm, m_data = new hkbBehaviorGraphData { m_stringData = new hkbBehaviorGraphStringData() } };
        var root = new hkRootLevelContainer();
        root.m_namedVariants.Add(new hkRootLevelContainer.NamedVariant { m_name = "hkbBehaviorGraph", m_className = "hkbBehaviorGraph", m_variant = g });
        return root;
    }

    static hkbStateMachine Sm(hkRootLevelContainer r) => (hkbStateMachine)BehGraph.Graph(r).m_rootGenerator;
    static CustomManualSelectorGenerator Cm(hkRootLevelContainer r, string name) => (CustomManualSelectorGenerator)BehGraph.NamedNodes(r)["CustomManualSelectorGenerator:" + name];

    [Fact]
    public void GeneratorsAddedByEachSideToTheSameSelectorAreUnited()
    {
        var b = Graph(); var e = Graph(); var m = Graph();
        Cm(e, "Attack_CMSG").m_generators.Add(Clip("a000_003001"));
        Cm(m, "Attack_CMSG").m_generators.Add(Clip("a000_003002"));
        var notes = new List<string>();
        var r = BehaviorMerge.MergeGraphs(b, e, m, notes);
        var names = Cm(r, "Attack_CMSG").m_generators.Select(x => x!.m_name).ToList();
        Assert.Equal(new[] { "a000_003000", "a000_003001", "a000_003002" }, names);
    }

    [Fact]
    public void StateAddedByMmvBringsItsNewNodesAndReusesEvsExistingNodes()
    {
        var b = Graph(); var e = Graph(); var m = Graph();
        Cm(e, "Idle_CMSG").m_offsetType = CustomManualSelectorGenerator.OffsetType.WeaponCategoryRight;   // EV edits a node MMV's new state reuses
        var shared = Cm(m, "Idle_CMSG");
        Sm(m).m_states.Add(State("Guard", 2, Cmsg("Guard_CMSG", Clip("a000_007000"), shared)));
        var r = BehaviorMerge.MergeGraphs(b, e, m, new List<string>());
        var guard = Sm(r).m_states.Single(s => s!.m_name == "Guard");
        var gc = (CustomManualSelectorGenerator)guard!.m_generator!;
        Assert.Equal("a000_007000", gc.m_generators[0]!.m_name);
        Assert.Same(Cm(r, "Idle_CMSG"), gc.m_generators[1]);                         // EV's instance, not MMV's copy
        Assert.Equal(CustomManualSelectorGenerator.OffsetType.WeaponCategoryRight, Cm(r, "Idle_CMSG").m_offsetType);
        Assert.Equal(3, Sm(r).m_states.Count);
    }

    [Fact]
    public void FieldChangedByOneSideIsTakenAndByBothGoesToEv()
    {
        var b = Graph(); var e = Graph(); var m = Graph();
        var ce = (hkbClipGenerator)Cm(e, "Attack_CMSG").m_generators[0]!; var cm = (hkbClipGenerator)Cm(m, "Attack_CMSG").m_generators[0]!;
        ce.m_playbackSpeed = 1.2f; cm.m_playbackSpeed = 0.9f;     // both: EV wins
        cm.m_startTime = 0.25f;                                    // MMV only
        var notes = new List<string>();
        var r = BehaviorMerge.MergeGraphs(b, e, m, notes);
        var c = (hkbClipGenerator)Cm(r, "Attack_CMSG").m_generators[0]!;
        Assert.Equal(1.2f, c.m_playbackSpeed);
        Assert.Equal(0.25f, c.m_startTime);
        Assert.Contains(notes, n => n.Contains("m_playbackSpeed"));
    }

    [Fact]
    public void SameNamedNodesMmvAddedTwiceStayDistinct()
    {
        var b = Graph(); var e = Graph(); var m = Graph();
        Cm(m, "Idle_CMSG").m_generators.Add(Clip("a637_033030", 1));
        Cm(m, "Attack_CMSG").m_generators.Add(Clip("a637_033030", 2));
        var r = BehaviorMerge.MergeGraphs(b, e, m, new List<string>());
        var x = (hkbClipGenerator)Cm(r, "Idle_CMSG").m_generators[1]!; var y = (hkbClipGenerator)Cm(r, "Attack_CMSG").m_generators[1]!;
        Assert.Equal(1, x.m_playbackSpeed);
        Assert.Equal(2, y.m_playbackSpeed);
    }

    [Fact]
    public void ImportedClipsPointAtTheirAnimationInTheMergedNameTable()
    {
        var b = Graph(); var e = Graph(); var m = Graph();
        foreach (var g in new[] { b, e, m }) BehGraph.Graph(g).m_data!.m_stringData!.m_animationNames.Add(@"..\hkx/a000_000000.hkx");
        BehGraph.Graph(e).m_data!.m_stringData!.m_animationNames.Add(@"..\hkx/a000_003001.hkx");
        Cm(e, "Attack_CMSG").m_generators.Add(new hkbClipGenerator { m_name = "x1", m_animationName = "a000_003001", m_animationInternalId = 1 });
        BehGraph.Graph(m).m_data!.m_stringData!.m_animationNames.Add(@"..\hkx/a000_003002.hkx");
        Cm(m, "Attack_CMSG").m_generators.Add(new hkbClipGenerator { m_name = "x2", m_animationName = "a000_003002", m_animationInternalId = 1 });
        var r = BehaviorMerge.MergeGraphs(b, e, m, new List<string>());
        var names = BehGraph.Graph(r).m_data!.m_stringData!.m_animationNames;
        var x2 = (hkbClipGenerator)Cm(r, "Attack_CMSG").m_generators.Single(g => g!.m_name == "x2")!;
        Assert.True(names[x2.m_animationInternalId].EndsWith("a000_003002.hkx"), $"id={x2.m_animationInternalId} table=[{string.Join(" | ", names)}]");
        var x1 = (hkbClipGenerator)Cm(r, "Attack_CMSG").m_generators.Single(g => g!.m_name == "x1")!;
        Assert.EndsWith("a000_003001.hkx", names[x1.m_animationInternalId]);
    }

    [Fact]
    public void MmvAnimationIdsThatCollideWithEvsNumberingGetFreshIds()
    {
        // c0000 convention: the name table only lists vanilla animations; mods number new animations past it.
        var b = Graph(); var e = Graph(); var m = Graph();
        foreach (var g in new[] { b, e, m }) BehGraph.Graph(g).m_data!.m_stringData!.m_animationNames.Add("../hkx/a000_000000.hkx");
        Cm(e, "Attack_CMSG").m_generators.Add(new hkbClipGenerator { m_name = "x1", m_animationName = "a000_003001", m_animationInternalId = 5 });
        Cm(m, "Attack_CMSG").m_generators.Add(new hkbClipGenerator { m_name = "x2", m_animationName = "a000_003002", m_animationInternalId = 5 });
        Cm(m, "Idle_CMSG").m_generators.Add(new hkbClipGenerator { m_name = "x3", m_animationName = "a000_003001", m_animationInternalId = 6 });  // MMV also uses EV's new animation
        var r = BehaviorMerge.MergeGraphs(b, e, m, new List<string>());
        var gens = BehGraph.AllObjects(r).OfType<hkbClipGenerator>().ToDictionary(c => c.m_name!);
        Assert.Equal(5, gens["x1"].m_animationInternalId);
        Assert.Equal(6, gens["x2"].m_animationInternalId);
        Assert.Equal(5, gens["x3"].m_animationInternalId);
        Assert.Single(BehGraph.Graph(r).m_data!.m_stringData!.m_animationNames);
    }

    static void Events(hkRootLevelContainer g, params string[] names)
    {
        var d = BehGraph.Graph(g).m_data!;
        foreach (var n in names) { d.m_stringData!.m_eventNames.Add(n); d.m_eventInfos.Add(new hkbEventInfo()); }
    }

    static void Vars(hkRootLevelContainer g, params string[] names)
    {
        var d = BehGraph.Graph(g).m_data!;
        d.m_variableInitialValues ??= new hkbVariableValueSet();
        foreach (var n in names)
        {
            d.m_stringData!.m_variableNames.Add(n);
            d.m_variableInfos.Add(new hkbVariableInfo { m_type = hkbVariableInfo.VariableType.VARIABLE_TYPE_INT32 });
            d.m_variableBounds.Add(new hkbVariableBounds());
            d.m_variableInitialValues.m_wordVariableValues.Add(new hkbVariableValue { m_value = n.Length });
        }
    }

    [Fact]
    public void ImportedNodesHaveEventAndVariableIndicesRenumberedToEvsLists()
    {
        var b = Graph(); var e = Graph(); var m = Graph();
        Events(b, "A", "B", "C"); Events(e, "A", "C"); Events(m, "A", "B", "C", "D");       // EV dropped B; MMV added D
        Vars(b, "x", "y"); Vars(e, "y"); Vars(m, "x", "y", "zz");                            // EV dropped x; MMV added zz
        var cmsg = Cmsg("New_CMSG", Clip("a000_009000"));
        cmsg.m_variableBindingSet = new hkbVariableBindingSet();
        cmsg.m_variableBindingSet.m_bindings.Add(new hkbVariableBindingSet.Binding { m_memberPath = "selectedGeneratorIndex", m_variableIndex = 1 });   // y
        cmsg.m_variableBindingSet.m_bindings.Add(new hkbVariableBindingSet.Binding { m_memberPath = "animId", m_variableIndex = 2 });                   // zz
        var st = State("New", 5, cmsg);
        st.m_transitions = new hkbStateMachine.TransitionInfoArray();
        st.m_transitions.m_transitions.Add(new hkbStateMachine.TransitionInfo { m_eventId = 2 });   // C
        st.m_transitions.m_transitions.Add(new hkbStateMachine.TransitionInfo { m_eventId = 3 });   // D
        Sm(m).m_states.Add(st);

        var notes = new List<string>();
        var r = BehaviorMerge.MergeGraphs(b, e, m, notes);

        var data = BehGraph.Graph(r).m_data!;
        Assert.Equal(new[] { "A", "C", "D" }, data.m_stringData!.m_eventNames);
        Assert.Equal(3, data.m_eventInfos.Count);
        Assert.Equal(new[] { "y", "zz" }, data.m_stringData.m_variableNames);
        Assert.Equal(2, data.m_variableInfos.Count);
        Assert.Equal(2, data.m_variableBounds.Count);
        Assert.Equal(2, data.m_variableInitialValues!.m_wordVariableValues[1].m_value);
        var ns = Sm(r).m_states.Single(s => s!.m_name == "New")!;
        Assert.Equal(new[] { 1, 2 }, ns.m_transitions!.m_transitions.Select(t => t.m_eventId));
        var nc = (CustomManualSelectorGenerator)ns.m_generator!;
        Assert.Equal(new[] { 0, 1 }, nc.m_variableBindingSet!.m_bindings.Select(x => x.m_variableIndex));
    }

    [Fact]
    public void NodeEvEditedThatMmvRemovedFromAListIsKept()
    {
        var b = Graph(); var e = Graph(); var m = Graph();
        Cm(b, "Attack_CMSG").m_generators.Add(Clip("a000_003001")); Cm(e, "Attack_CMSG").m_generators.Add(Clip("a000_003001", 1.3f));  // EV retunes it
        var notes = new List<string>();
        var r = BehaviorMerge.MergeGraphs(b, e, m, notes);                                                                                  // MMV never had it after base? (m = base without it)
        Assert.Contains(Cm(r, "Attack_CMSG").m_generators, g => g!.m_name == "a000_003001");
        Assert.Contains(notes, n => n.Contains("deleted") && n.Contains("kept"));
    }
}
