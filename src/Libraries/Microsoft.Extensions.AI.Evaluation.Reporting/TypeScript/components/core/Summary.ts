// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import uFuzzy from "@leeoniya/ufuzzy";
import { isLeafFailed } from "./scoring";

export enum ScoreNodeType {
    Group,
    Scenario,
    Iteration,
}

export type ScoreSummary = {
    primaryResult: ScoreNode;
    includesReportHistory: boolean;
    executionHistory: Map<string, ScoreNode>;
    nodesByKey: Map<string, Map<string, ScoreNode>>;
    reverseTextIndex: ReverseTextIndex
    reverseTextIndexByExecution: Map<string, ReverseTextIndex>
};

const appendNodeKey = (nodeKey: string, segment: string): string =>
    `${nodeKey}.${segment.replace(/%/g, "%25").replace(/\./g, "%2E")}`;

export class ScoreNode {
    constructor(name: string, nodeType: ScoreNodeType, nodeKey: string, executionName: string) {
        this.name = name;
        this.nodeType = nodeType;
        this.children = new Map<string, ScoreNode>();
        this.nodeKey = nodeKey;
        this.executionName = executionName;
    }
    private children: Map<string, ScoreNode>;

    nodeType: ScoreNodeType;
    name: string;
    executionName: string;
    nodeKey: string;

    scenario?: ScenarioRunResult;

    shortenedPrompt?: string;
    failed: boolean = false;
    numPassingIterations: number = 0;
    numFailingIterations: number = 0;

    setChildren(children: Map<string, ScoreNode>) {
        this.children = children;
    }

    insertNode(path: string[], scenario: ScenarioRunResult) {
        if (path.length === 0) {
            return;
        }

        const [head, ...tail] = path;

        if (tail.length === 0) {
            // A dotted scenario name shares the tree with iteration names, so the same node can already
            // hold a result. Suffix rather than overwrite so no run is lost to a collision or a duplicate.
            let key = head;
            for (let i = 2; this.children.get(key)?.scenario !== undefined; i++) {
                key = `${head} (${i})`;
            }
            let leaf = this.children.get(key);
            if (!leaf) {
                leaf = new ScoreNode(key, ScoreNodeType.Iteration, appendNodeKey(this.nodeKey, key), this.executionName);
                this.children.set(key, leaf);
            }
            leaf.scenario = scenario;
            return;
        }

        const child = this.children.get(head);
        if (child) {
            child.insertNode(tail, scenario);
        } else {
            const nodeType = tail.length === 1 ? ScoreNodeType.Scenario : ScoreNodeType.Group;
            const newChild = new ScoreNode(head, nodeType, appendNodeKey(this.nodeKey, head), this.executionName);
            newChild.insertNode(tail, scenario);
            this.children.set(head, newChild);
        }
    }

    get isLeafNode() {
        return this.scenario !== undefined;
    }

    get childNodes() {
        return [...this.children.values()];
    }

    get hasChildNodes() {
        return this.children.size > 0;
    }

    get flattenedNodes() {
        return [...flattener(this)];
    }

    aggregate() {
        this.failed = false;
        this.numPassingIterations = 0;
        this.numFailingIterations = 0;

        const scenario = this.scenario;
        if (scenario) {
            this.failed = isLeafFailed(scenario);

            this.numPassingIterations = this.failed ? 0 : 1;
            this.numFailingIterations = this.failed ? 1 : 0;
            const lastMessage = scenario.messages[scenario.messages.length - 1];

            const { messages } = getConversationDisplay(lastMessage ? [lastMessage] : [], scenario.modelResponse);
            let history = "";
            if (messages.length === 1) {
                const content = messages[0].content;
                if (isTextContent(content)) {
                    history = content.text;
                }
            } else if (messages.length > 1) {
                history = messages
                    .filter(m => isTextContent(m.content))
                    .map(m => `[${m.participantName}] ${(m.content as TextContent).text}`)
                    .join("\n\n");
            }

            this.shortenedPrompt = shortenPrompt(history);
        }

        for (const child of this.childNodes) {
            child.aggregate();
            if (child.numPassingIterations + child.numFailingIterations > 0) {
                this.failed = this.failed || child.failed;
                this.numPassingIterations += child.numPassingIterations;
                this.numFailingIterations += child.numFailingIterations;
                if (!scenario && this.nodeType == ScoreNodeType.Scenario) {
                    this.shortenedPrompt = child.shortenedPrompt;
                }
            }
        }
    }

};

export class ReverseTextIndex {

    private stringsToSearch: string[] = [];
    private keys: string[] = [];

    addText(key: string, text?: string) {
        if (!text) {
            return;
        }
        this.stringsToSearch.push(text);
        this.keys.push(key);
    }

    search(searchValue: string): Set<string> {
        const opts = {
            intraMode: 0,
            unicode: true,
        } as uFuzzy.Options;
        const fz = new uFuzzy(opts);
        const terms = fz.split(searchValue);
        const keys = new Set<string>();
        for (const term of terms) {
            const searchResult = fz.search(this.stringsToSearch, term) as uFuzzy.FilteredResult;
            const matches = searchResult[0];
            for (const match of matches) {
                keys.add(this.keys[match]);
            }
        }
        return keys;
    }
}

export const createScoreSummary = (dataset: Dataset): ScoreSummary => {

    if (!dataset?.scenarioRunResults || dataset.scenarioRunResults.length === 0) {
        const emptyRoot = new ScoreNode("All Evaluations", ScoreNodeType.Group, "root", "");
        return {
            primaryResult: emptyRoot,
            includesReportHistory: false,
            executionHistory: new Map<string, ScoreNode>(),
            nodesByKey: new Map<string, Map<string, ScoreNode>>(),
            reverseTextIndex: new ReverseTextIndex(),
            reverseTextIndexByExecution: new Map<string, ReverseTextIndex>(),
        };
    }

    const executionHistory = new Map<string, ScoreNode>();
    for (const scenario of dataset.scenarioRunResults) {
        const executionName = scenario.executionName;
        if (!executionHistory.has(executionName)) {
            const newRoot = new ScoreNode(`All Evaluations [${executionName}]`, ScoreNodeType.Group, "root", executionName);
            executionHistory.set(executionName, newRoot);
        }
        const scoreNode = executionHistory.get(executionName)!;
        const path = [...scenario.scenarioName.split('.'), scenario.iterationName];
        scoreNode.insertNode(path, scenario);
    }

    const nodesByKey = new Map<string, Map<string, ScoreNode>>();
    for (const executionName of executionHistory.keys()) {
        nodesByKey.set(executionName, new Map<string, ScoreNode>());
    }

    for (const scoreNode of executionHistory.values()) {
        scoreNode.aggregate();

        for (const node of scoreNode.flattenedNodes) {
            const nodeList = nodesByKey.get(node.executionName)!;
            nodeList.set(node.nodeKey, node);
        }
    }

    const [primaryResult] = executionHistory.values();
    const reverseTextIndexByExecution = new Map<string, ReverseTextIndex>();
    for (const [executionName, scoreNode] of executionHistory.entries()) {
        const index = new ReverseTextIndex();
        buildReverseTextIndex(index, scoreNode);
        reverseTextIndexByExecution.set(executionName, index);
    }

    const reverseTextIndex = reverseTextIndexByExecution.get(primaryResult.executionName)!;

    return {
        primaryResult,
        includesReportHistory: executionHistory.size > 1,
        executionHistory,
        nodesByKey,
        reverseTextIndex,
        reverseTextIndexByExecution,
    };
};

const buildReverseTextIndex = (index: ReverseTextIndex, root: ScoreNode): void => {
    for (const node of root.flattenedNodes) {
        index.addText(node.nodeKey, node.scenario?.scenarioName);
        index.addText(node.nodeKey, node.scenario?.iterationName);
        for (const message of node.scenario?.messages ?? []) {
            for (const content of message.contents) {
                if (isTextContent(content)) {
                    index.addText(node.nodeKey, content.text);
                }
            }
        }
        for (const message of node.scenario?.modelResponse?.messages ?? []) {
            for (const content of message.contents) {
                if (isTextContent(content)) {
                    index.addText(node.nodeKey, content.text);
                }
            }
        }
    }
};

export const getScoreHistory = (scoreSummary: ScoreSummary, scenario: ScenarioRunResult): Map<string, ScenarioRunResult> => {

    const scenarioName = scenario.scenarioName;
    const iterationName = scenario.iterationName;

    const scoreHistory = new Map<string, ScenarioRunResult>();
    for (const [key, node] of scoreSummary.executionHistory.entries()) {
        for (const leafNode of node.flattenedNodes) {
            if (leafNode.scenario?.scenarioName == scenarioName &&
                leafNode.scenario?.iterationName == iterationName) {
                scoreHistory.set(key, leafNode.scenario);
            }
        }
    }
    return scoreHistory;
};

const shortenPrompt = (prompt: string | undefined) => {
    if (!prompt) {
        return "";
    }
    if (prompt.length > 80) {
        return prompt.substring(0, 80) + "...";
    }
    return prompt;
};

const flattener = function* (node: ScoreNode): Iterable<ScoreNode> {
    yield node;
    for (const child of node.childNodes) {
        yield* flattener(child);
    }
};

const isContentObject = (content: AIContent): content is AIContent & Record<string, unknown> =>
    typeof content === "object" && content !== null;

export const isTextContent = (content: AIContent): content is TextContent =>
    isContentObject(content) && typeof content.text === "string";

export const isImageContent = (content: AIContent): content is UriContent | DataContent => {
    if (!isContentObject(content) || typeof content.uri !== "string") {
        return false;
    }

    return (typeof content.mediaType === "string" && content.mediaType.startsWith("image/")) ||
        content.uri.startsWith('data:image/');
};

export type ConversationDisplay = {
    messages: ChatMessageDisplay[];
    model?: string;
    usage?: UsageDetails;
};

export type ChatMessageDisplay = {
    role: string;
    participantName: string;
    content: AIContent;
};

export const getConversationDisplay = (messages: ChatMessage[], modelResponse?: ChatResponse): ConversationDisplay => {
    const chatMessages: ChatMessageDisplay[] = [];

    for (const m of messages) {
        for (const c of m.contents) {
            const participantName = m.authorName ? `${m.authorName} (${m.role})` : m.role;
            chatMessages.push({
                role: m.role,
                participantName: participantName,
                content: c
            });
        }
    }

    if (modelResponse?.messages) {
        for (const m of modelResponse.messages) {
            for (const c of m.contents) {
                const participantName = m.authorName ? `${m.authorName} (${m.role})` : m.role || 'Assistant';
                chatMessages.push({
                    role: m.role,
                    participantName: participantName,
                    content: c
                });
            }
        }
    }

    return {
        messages: chatMessages,
        model: modelResponse?.modelId,
        usage: modelResponse?.usage
    };
};
