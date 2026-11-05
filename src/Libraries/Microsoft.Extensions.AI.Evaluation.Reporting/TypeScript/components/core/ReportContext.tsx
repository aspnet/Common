// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { useContext, createContext, useState, useEffect, useMemo, useCallback, useRef } from "react";
import { ScoreNode, ScoreSummary } from "./Summary";
import { AnnouncerProvider } from "./Announcer";

export type ReportView = 'overview' | 'cases' | 'history' | 'comparison';

export type ReportContextType = {
    dataset: Dataset,
    scoreSummary: ScoreSummary,
    selectedScenarioLevel?: string,
    selectScenarioLevel: (key: string) => void,
    clearScenarioLevel: () => void,
    renderMarkdown: boolean,
    setRenderMarkdown: (renderMarkdown: boolean) => void,
    prettifyJson: boolean,
    setPrettifyJson: (prettifyJson: boolean) => void,
    searchValue: string,
    setSearchValue: (searchValue: string) => void,
    selectedTags: string[],
    handleTagClick: (tag: string) => void,
    clearFilters: () => void,
    filterTree: (node: ScoreNode) => ScoreNode | null,
    view: ReportView,
    setView: (view: ReportView) => void,
    darkMode: boolean,
    setDarkMode: (darkMode: boolean) => void,
    failedOnly: boolean,
    setFailedOnly: (failedOnly: boolean) => void,
    casePage: number,
    setCasePage: (casePage: number) => void,
    cmpA?: string,
    setCmpA: (cmpA: string | undefined) => void,
    cmpB?: string,
    setCmpB: (cmpB: string | undefined) => void,
    exec?: string,
    setExec: (exec: string | undefined) => void,
    activeExecution: string,
    activeNode: ScoreNode,
    scopedNode: ScoreNode,
    isSettingsOpen: boolean,
    setIsSettingsOpen: (isSettingsOpen: boolean) => void,
};

const ReportContext = createContext<ReportContextType | undefined>(undefined);

export const ReportContextProvider = ({ dataset, scoreSummary, persistKey, children }:
    { dataset: Dataset, scoreSummary: ScoreSummary, persistKey?: string, children: React.ReactNode }) => {

    const app = useProvideReportContext(dataset, scoreSummary, persistKey);

    return (
        <ReportContext.Provider value={app}>
            <AnnouncerProvider>
                {children}
            </AnnouncerProvider>
        </ReportContext.Provider>
    );
};

export const useReportContext = (): ReportContextType => {
    const context = useContext(ReportContext);
    if (context === undefined) {
        throw new Error("useReportContext must be used within a ReportContextProvider");
    }
    return context;
};

const STORAGE_PREFIX = 'ai-eval-report:ui-intent:';

type PersistedUiIntent = {
    view: ReportView;
    darkMode: boolean;
    searchValue: string;
    selectedTags: string[];
    failedOnly: boolean;
    casePage: number;
    cmpA?: string;
    cmpB?: string;
    exec?: string;
};

const readPersisted = (persistKey: string | undefined): Partial<PersistedUiIntent> | null => {
    if (!persistKey) return null;
    try {
        const raw = sessionStorage.getItem(STORAGE_PREFIX + persistKey);
        return raw ? (JSON.parse(raw) as Partial<PersistedUiIntent>) : null;
    } catch {
        return null;
    }
};

const writePersisted = (persistKey: string | undefined, value: PersistedUiIntent): void => {
    if (!persistKey) return;
    try {
        sessionStorage.setItem(STORAGE_PREFIX + persistKey, JSON.stringify(value));
    } catch {
        // storage may be unavailable (private browsing, quota exceeded); persistence is best-effort.
    }
};

const useProvideReportContext = (
    dataset: Dataset,
    scoreSummary: ScoreSummary,
    persistKey: string | undefined,
): ReportContextType => {
    const [selectedScenarioLevel, setSelectedScenarioLevel] = useState<string | undefined>(undefined);
    const [renderMarkdown, setRenderMarkdown] = useState<boolean>(true);
    const [prettifyJson, setPrettifyJson] = useState<boolean>(true);
    const [selectedTags, setSelectedTags] = useState<string[]>([]);
    const [searchValue, setSearchValueRaw] = useState<string>("");

    const [view, setView] = useState<ReportView>('overview');
    const [darkMode, setDarkMode] = useState<boolean>(false);
    const [failedOnly, setFailedOnly] = useState<boolean>(false);
    const [casePage, setCasePage] = useState<number>(1);
    const [cmpA, setCmpA] = useState<string | undefined>(undefined);
    const [cmpB, setCmpB] = useState<string | undefined>(undefined);
    const [exec, setExec] = useState<string | undefined>(undefined);
    const [isSettingsOpen, setIsSettingsOpen] = useState<boolean>(false);

    const hydratedKey = useRef<string | undefined>(undefined);
    useEffect(() => {
        if (!persistKey || hydratedKey.current === persistKey) return;
        hydratedKey.current = persistKey;
        const stored = readPersisted(persistKey);
        if (!stored) return;
        if (stored.view !== undefined) setView(stored.view);
        if (stored.darkMode !== undefined) setDarkMode(stored.darkMode);
        if (stored.searchValue !== undefined) setSearchValueRaw(stored.searchValue);
        if (stored.selectedTags !== undefined) setSelectedTags(stored.selectedTags);
        if (stored.failedOnly !== undefined) setFailedOnly(stored.failedOnly);
        if (stored.casePage !== undefined) setCasePage(stored.casePage);
        if (stored.cmpA !== undefined) setCmpA(stored.cmpA);
        if (stored.cmpB !== undefined) setCmpB(stored.cmpB);
        if (stored.exec !== undefined) setExec(stored.exec);
    }, [persistKey]);

    useEffect(() => {
        writePersisted(persistKey, {
            view, darkMode, searchValue, selectedTags, failedOnly, casePage, cmpA, cmpB, exec,
        });
    }, [persistKey, view, darkMode, searchValue, selectedTags, failedOnly, casePage, cmpA, cmpB, exec]);

    const selectScenarioLevel = (key: string) => {
        if (key === selectedScenarioLevel) {
            setSelectedScenarioLevel(undefined);
        } else {
            setSelectedScenarioLevel(key);
        }
    };

    const clearScenarioLevel = () => setSelectedScenarioLevel(undefined);

    const setSearchValue = (next: string) => {
        setSearchValueRaw(next);
        if (next !== "") {
            setView('cases');
            setCasePage(1);
        }
    };

    const handleTagClick = (tag: string) => {
        setSelectedTags((prevTags) =>
          prevTags.includes(tag) ? prevTags.filter((t) => t !== tag) : [...prevTags, tag]
        );
    };

    const clearFilters = () => {
        setSelectedTags([]);
        setSearchValueRaw("");
    };

    const activeExecution = exec ?? scoreSummary.primaryResult.executionName;
    const activeNode = scoreSummary.executionHistory.get(activeExecution) ?? scoreSummary.primaryResult;

    const scopedNode = useMemo(() => {
        if (!selectedScenarioLevel) return activeNode;
        return activeNode.flattenedNodes.find(n => n.nodeKey === selectedScenarioLevel) ?? activeNode;
    }, [activeNode, selectedScenarioLevel]);

    const filterTree = useCallback((node: ScoreNode): ScoreNode | null => {
        if (selectedTags.length === 0 && searchValue === "") {
            return node;
        }

        const searchIndex =
            scoreSummary.reverseTextIndexByExecution.get(activeExecution) ?? scoreSummary.reverseTextIndex;
        const searchedNodes = searchIndex.search(searchValue);

        const srch = (node: ScoreNode) : ScoreNode | null => {
            if (node.isLeafNode) {
                const tagMatches = selectedTags.length > 0 && node.scenario?.tags?.some(tag => selectedTags.includes(tag));
                const searchMatches = searchValue !== "" && searchedNodes.has(node.nodeKey);
                return tagMatches || searchMatches ? node : null;
            }

            const filteredChildren = node.childNodes
                .map(srch)
                .filter((child): child is ScoreNode => child !== null);

            if (filteredChildren.length > 0) {
                const newNode = new ScoreNode(node.name, node.nodeType, node.nodeKey, node.executionName);
                newNode.setChildren(new Map(filteredChildren.map(child => [child.name, child])));
                newNode.aggregate();
                return newNode;
            }

            return null;
        };

        return srch(node);
    }, [selectedTags, searchValue, scoreSummary, activeExecution]);

    return {
        dataset,
        scoreSummary,
        selectedScenarioLevel,
        selectScenarioLevel,
        clearScenarioLevel,
        renderMarkdown,
        setRenderMarkdown,
        prettifyJson,
        setPrettifyJson,
        searchValue,
        setSearchValue,
        selectedTags,
        handleTagClick,
        clearFilters,
        filterTree,
        view,
        setView,
        darkMode,
        setDarkMode,
        failedOnly,
        setFailedOnly,
        casePage,
        setCasePage,
        cmpA,
        setCmpA,
        cmpB,
        setCmpB,
        exec,
        setExec,
        activeExecution,
        activeNode,
        scopedNode,
        isSettingsOpen,
        setIsSettingsOpen,
    };
};
