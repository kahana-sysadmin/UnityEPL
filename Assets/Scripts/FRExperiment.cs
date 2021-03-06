using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using System.IO;
using UnityEngine; // to read resource files packaged with Unity


public class FRExperiment : ExperimentBase {
  protected List<string> source_words;
  protected List<string> blank_words;
  protected RepCounts rep_counts = null;
  protected int words_per_list;
  protected int unique_words_per_list;

  protected Session currentSession;

  public FRExperiment(InterfaceManager _manager) : base(_manager) {
    // Repetition specification:
    int[] repeats = manager.GetSetting("wordRepeats").ToObject<int[]>();
    int[] counts = manager.GetSetting("wordCounts").ToObject<int[]>();

    if(repeats.Length != counts.Length) {
      throw new Exception("Word Repeats and Counts not aligned");
    }

    for(int i=0; i < repeats.Length; i++) {
      if(rep_counts == null) {
        rep_counts = new RepCounts(repeats[i], counts[i]);
      }
      else {
        rep_counts = rep_counts.RepCnt(repeats[i], counts[i]);
      }
    }
    words_per_list = rep_counts.TotalWords();
    unique_words_per_list = rep_counts.UniqueWords();

    blank_words =
      new List<string>(Enumerable.Repeat(string.Empty, words_per_list));


    string source_list = manager.fileManager.GetWordList();
    source_words = new List<string>();

    //skip line for csv header
    foreach(var line in File.ReadLines(source_list).Skip(1))
    {
      source_words.Add(line);
    }

    // copy wordpool to session directory
    // TODO: we should just do a direct file copy...
    
    string path = System.IO.Path.Combine(manager.fileManager.SessionPath(), "wordpool.txt");
    if(!System.IO.File.Exists(path)) {
      using(TextWriter tw = new StreamWriter(path))
      {
        tw.WriteLine("word"); // header
        foreach (String s in source_words) {
          tw.WriteLine(s);
        }
      }
    }

    // load state if previously existing
    dynamic loadedState = LoadState((string)manager.GetSetting("participantCode"), (int)manager.GetSetting("session"));
    if(loadedState != null) {
      state = loadedState;
      currentSession = LoadSession((string)manager.GetSetting("participantCode"), (int)manager.GetSetting("session"));

      // log experiment resume
      manager.Do(new EventBase<string, Dictionary<string, object>>(manager.ReportEvent, "experiment resumed", null));
      ReportEvent("experiment resume", null);

      state.listIndex++;
    }
    else {
      currentSession = GenerateSession();
      state.listIndex = 0;
    }

    state.runIndex = 0;
    state.wordIndex = 0;
    state.mainLoopIndex = 0;
    state.micTestIndex = 0;

    stateMachine["Run"] = new List<Action> {DoIntroductionPrompt,
                                            DoIntroductionVideo,
                                            DoRepeatVideo,
                                            DoMicrophoneTest,
                                            DoRepeatMicTest,
                                            DoQuitorContinue,
                                            MainLoop,
                                            Quit};

    stateMachine["MainLoop"] = new List<Action> {DoNextListPrompt,
                                                 DoStartTrial,
                                                 DoRest,
                                                 DoCountdownVideo,
                                                 DoEncodingDelay,
                                                 DoEncoding,
                                                 DistractorTimeout,
                                                 DoDistractor,
                                                 DoRecallPrompt,
                                                 DoRecall,
                                                 DoEndTrial};

    stateMachine["MicrophoneTest"] = new List<Action> {DoMicTestPrompt,
                                                       DoRecordTest,
                                                       DoPlaybackTest};

    Start();

    // TODO: some of these functions could be re imagined with wrappers, where the
    // state machine has functions that take parameters and return functions, such
    // as using a single function for the 'repeatlast' state that takes a prompt
    // to show. This would separate experiment and state machine logic, so state
    // variables are incremented in wrappers rather than experiment functions.
    // It's not clear whether the reality of this improves clarity or reusability,
    // so I've defferred this. If it makes sense to do this or make more use of
    // wrapper functions that add state machine information, please do.

    Dictionary<string, object> data = new Dictionary<string, object>();
    data.Add("session", (int) manager.GetSetting("session"));
    SendHostPCMessage("SESSION", data);

    Do(new EventBase(Run));
  }

  //////////
  // Wait Functions
  //////////

  protected void DoPauseBeforeRecall() {
    int[] limits = manager.GetSetting("recallDelay").ToObject<int[]>();
    int interval = InterfaceManager.rnd.Next(limits[0], limits[1]);
    state.mainLoopIndex++;
    WaitForTime(interval);
  }

  protected void DoEncodingDelay() {
    int[] limits = manager.GetSetting("stimulusInterval").ToObject<int[]>(); 
    int interval = InterfaceManager.rnd.Next(limits[0], limits[1]);
    state.mainLoopIndex++;

    SendHostPCMessage("ISI", null);
    WaitForTime(interval);
  }  


  protected void DoRest() {
    int duration = (int)manager.GetSetting("restDuration");
    state.mainLoopIndex++;
    manager.Do(new EventBase<string, string>(manager.ShowText, "orientation stimulus", "+"));
    ReportEvent("rest", null);
    SendHostPCMessage("REST", null);

    DoIn(new EventBase(() => {
                                manager.Do(new EventBase(manager.ClearText)); 

                                ReportEvent("rest end", null);

                                Run();
                            }), 
                            duration);
  }

  //////////
  // List Setup Functions
  //////////

  protected void DoStartTrial() {
    Dictionary<string, object> data = new Dictionary<string, object>();
    data.Add("trial", state.listIndex);
    data.Add("stim", currentSession[state.listIndex].encoding_stim);
    // TODO: recall stim

    ReportEvent("start trial", data);
    SendHostPCMessage("TRIAL", data);
    state.mainLoopIndex++;
    Run();
  }

  protected void DoEndTrial() {
    SendHostPCMessage("TRIALEND", null);
    state.mainLoopIndex++;
    Run();
  }

  //////////
  // Text prompts and associated key handlers
  //////////
  protected void DoConfirmStart() {
    ConfirmStart();
  }

  protected void DoQuitorContinue(){
    state.runIndex++;
    QuitPrompt();
  }

  protected void DoMicTestPrompt() {
    state.micTestIndex++;
    MicTestPrompt();
  }

  protected void DoRepeatVideo() {
    WaitForKey("repeat introduction video", "Press Y to continue to practice list, \n Press N to replay instructional video.", 
                RepeatOrContinue);
  }

  protected void DoRepeatMicTest() {
    WaitForKey("repeat mic test", "Did you hear the recording? \n(Y=Continue / N=Try Again).", 
                RepeatOrContinue);
  }

  protected void DoIntroductionPrompt() {
    state.runIndex++;
    WaitForKey("show instruction video", "Press any key to show instruction video", AnyKey);
  }

  protected void DoRecallPrompt() {
    state.mainLoopIndex++;
    base.RecallPrompt();
  }

  protected void DoNextListPrompt() {
    state.mainLoopIndex++; 
    if(state.listIndex == 0) {
      WaitForKey("pause before list", "Press any key for practice trial.", AnyKey);
    }
    else {
      WaitForKey("pause before list", "Press any key for trial " + (string)state.listIndex.ToString() + ".", AnyKey);
    }
  }

  //////////
  // Video Presentation functions
  //////////

  protected void DoIntroductionVideo() {
    state.runIndex++;
    base.IntroductionVideo();
  }

  protected void DoCountdownVideo() {
    state.mainLoopIndex++;
    base.CountdownVideo();
  }

  //////////
  // Top level functions for state machine loops
  //////////

  protected void MainLoop() {
    bool loop = CheckLoop();
    if(loop) {
      stateMachine["MainLoop"][state.mainLoopIndex].Invoke();
    }
  }

  protected bool CheckLoop() {
    if(state.mainLoopIndex == stateMachine["MainLoop"].Count) {
      state.mainLoopIndex = 0;
      state.listIndex++;

      if(state.listIndex == 0) {
        Do(new EventBase(DoConfirmStart));
        return false;
      }
    }

    if(state.listIndex  == currentSession.Count) {
      state.runIndex++;
      state.isComplete = true;

      this.Do(new EventBase(Run));
      return false;
    }

    return true;
  }

  protected void DoMicrophoneTest() {
    if(state.micTestIndex == stateMachine["MicrophoneTest"].Count()) {
      state.runIndex++;
      state.micTestIndex = 0;
      Run();
      return;
    } 
    else {
      stateMachine["MicrophoneTest"][state.micTestIndex].Invoke();
    }
  }

  //////////
  // Experiment presentation stages
  //////////

  protected void DoOrientation() {
    state.mainLoopIndex++;
    base.Orientation();
  }

  protected void DoEncoding() {

    StimWordList currentList = currentSession[state.listIndex].encoding;

    if(state.wordIndex >= currentList.Count) {
      state.wordIndex = 0;
      state.mainLoopIndex++;
      Run();
      return;
    }

    Encoding(currentList, state.wordIndex);
    state.wordIndex++;
  }

  protected void DoDistractor() {
    base.Distractor();
  }

  protected void DistractorTimeout() {
    DoIn(new EventBase(() => state.mainLoopIndex++), (int)manager.GetSetting("distractorDuration"));
    state.mainLoopIndex++;
    Run();
  }

  protected void DoRecall() {
    state.mainLoopIndex++;
    string path = System.IO.Path.Combine(manager.fileManager.SessionPath(), state.listIndex.ToString() + ".wav");
    Recall(path);
  }

  //////////
  // Microphone testing states
  //////////

  protected void DoRecordTest() {
    state.micTestIndex++;
    string file =  System.IO.Path.Combine(manager.fileManager.SessionPath(), "microphone_test_" 
                    + DataReporter.TimeStamp().ToString("yyyy-MM-dd_HH_mm_ss") + ".wav");

    state.recordTestPath = file;
    RecordTest(file);
  }

  protected void DoPlaybackTest() {
    state.micTestIndex++;
    string file = state.recordTestPath;
    PlaybackTest(file);
  }

  //////////
  // state-specific key handlers
  //////////

  protected void RepeatOrContinue(string key, bool down) {
    if(down && key=="N") {
      state.runIndex--;
      Do(new EventBase(Run));
    }
    else if(down && key=="Y") {
      state.runIndex++;
      manager.Do(new EventBase(manager.ClearText));
      Do(new EventBase(Run));
    }
    else {
      manager.RegisterKeyHandler(RepeatOrContinue);
    }
  }

  //////////
  // Experiment specific saving and loading logic
  //////////

  public override void SaveState() {
    base.SaveState();
    SaveSession();
  }

  public void SaveSession() {
    string filename = System.IO.Path.Combine(manager.fileManager.SessionPath(), "session_words.json");
    JsonSerializer serializer = new JsonSerializer();

    // create .lst files for annotation scripts
    for(int i = 0; i < currentSession.Count; i++) {
      string lstfile = System.IO.Path.Combine(manager.fileManager.SessionPath(), i.ToString() + ".lst");
      IList<string> noRepeats = new HashSet<string>(currentSession[i].encoding.words).ToList();
      WriteAllLinesNoExtraNewline(lstfile, noRepeats); 
    }

    using (StreamWriter sw = new StreamWriter(filename))
      using (JsonWriter writer = new JsonTextWriter(sw))
      {
        serializer.Serialize(writer, currentSession);
      }
  }

  public Session LoadSession(string participant, int session) {
    if(System.IO.File.Exists(System.IO.Path.Combine(manager.fileManager.SessionPath(participant, session), "session_words.json"))) {
      string json = System.IO.File.ReadAllText(System.IO.Path.Combine(manager.fileManager.SessionPath(participant, session), "session_words.json"));
      return JsonConvert.DeserializeObject<Session>(json);
    }
    else{
      return null;
    }
  }

  protected static void WriteAllLinesNoExtraNewline(string path, IList<string> lines)
    {
        if (path == null)
            throw new UnityException("path argument should not be null");
        if (lines == null)
            throw new UnityException("lines argument should not be null");

        using (var stream = System.IO.File.OpenWrite(path))
        {
            using (System.IO.StreamWriter writer = new System.IO.StreamWriter(stream))
            {
                if (lines.Count > 0)
                {
                    for (int i = 0; i < lines.Count - 1; i++)
                    {
                        writer.WriteLine(lines[i]);
                    }
                    writer.Write(lines[lines.Count - 1]);
                }
            }
        }
    }

  //////////
  // Word/Stim list generation
  //////////

  public RepRun MakeRun(RandomSubset subset_gen, bool enc_stim,
      bool rec_stim) {
    var enclist = RepWordGenerator.Generate(rep_counts,
        subset_gen.Get(unique_words_per_list), enc_stim);
    var reclist = RepWordGenerator.Generate(rep_counts, blank_words, rec_stim);
    return new RepRun(enclist, reclist, enc_stim, rec_stim);
  }


  public Session GenerateSession() {
    // Parameters retrieved from experiment config, given default
    // value if null.
    // Numbers of list types:
    int practice_lists = (int)(manager.GetSetting("practiceLists") ?? 1);
    int pre_no_stim_lists = (int)(manager.GetSetting("preNoStimLists") ?? 3);
    int encoding_only_lists = (int)(manager.GetSetting("encodingOnlyLists") ?? 4);
    int retrieval_only_lists = (int)(manager.GetSetting("retrievalOnlyLists") ?? 4);
    int encoding_and_retrieval_lists = (int)(manager.GetSetting("encodingAndRetrievalLists") ?? 4);
    int no_stim_lists = (int)(manager.GetSetting("noStimLists") ?? 10);
    
    RandomSubset subset_gen = new RandomSubset(source_words);


    var session = new Session();

    for (int i=0; i<practice_lists; i++) {
      session.Add(MakeRun(subset_gen, false, false));
    }
          
    for (int i=0; i<pre_no_stim_lists; i++) {
      session.Add(MakeRun(subset_gen, false, false));
    }

    var randomized_list = new Session();

    for (int i=0; i<encoding_only_lists; i++) {
      randomized_list.Add(MakeRun(subset_gen, true, false));
    }

    for (int i=0; i<retrieval_only_lists; i++) {
      randomized_list.Add(MakeRun(subset_gen, false, true));
    }

    for (int i=0; i<encoding_and_retrieval_lists; i++) {
      randomized_list.Add(MakeRun(subset_gen, true, true));
    }

    for (int i=0; i<no_stim_lists; i++) {
      randomized_list.Add(MakeRun(subset_gen, false, false));
    }

    session.AddRange(RepWordGenerator.Shuffle(randomized_list));

    return session;
  }
}
