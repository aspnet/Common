// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

import { useCallback, useEffect, useId, useLayoutEffect, useMemo, useRef, useState } from 'react';
import {
    FluentProvider,
    makeStyles,
    mergeClasses,
    Button,
    Tooltip,
    Switch,
    Badge,
    Dropdown,
    Option,
    Drawer,
    DrawerBody,
    DrawerHeader,
    DrawerHeaderTitle,
    useArrowNavigationGroup,
    useRestoreFocusTarget,
    useRestoreFocusSource,
} from '@fluentui/react-components';
import {
    Settings28Regular,
    DismissRegular,
    ArrowDownloadRegular,
    WeatherMoonRegular,
    WeatherSunnyRegular,
} from '@fluentui/react-icons';
import { useReportContext, type ReportView } from '../core/ReportContext';
import type { ScoreNode } from '../core/Summary';
import { useAnnounce } from '../core/Announcer';
import { srOnlyStyle } from '../styles/reportStyles';
import { resolveTheme, detectHostDarkMode } from './theme';
import { useAdoResize } from './useAdoResize';
import { SidebarTree } from './SidebarTree';

export type HeightStrategy =
    | 'fill-viewport'
    | 'auto-grow';

export type ThemeSource =
    | 'toggle'
    | 'host';

const SIDEBAR_WIDTH = '274px';
const TOPBAR_HEIGHT = '48px';

const rootBase = {
    display: 'flex',
    flexDirection: 'column',
    backgroundColor: 'var(--acrylic-fallback-light)',
    color: 'var(--neutral-foreground-1)',
    fontFamily: 'var(--font-family-base)',
} as const;

const useStyles = makeStyles({
    skipLink: {
        position: 'absolute',
        top: 0,
        left: '8px',
        zIndex: 1000,
        boxSizing: 'border-box',
        maxWidth: 'calc(100% - 16px)',
        whiteSpace: 'nowrap',
        paddingTop: '8px',
        paddingBottom: '8px',
        paddingLeft: '16px',
        paddingRight: '16px',
        backgroundColor: 'var(--neutral-background-1)',
        color: 'var(--neutral-foreground-1)',
        boxShadow: 'var(--shadow-8, 0 2px 8px rgba(0, 0, 0, 0.25))',
        borderTopLeftRadius: 'var(--radius-medium, 4px)',
        borderTopRightRadius: 'var(--radius-medium, 4px)',
        borderBottomLeftRadius: 'var(--radius-medium, 4px)',
        borderBottomRightRadius: 'var(--radius-medium, 4px)',
        textDecorationLine: 'none',
        transform: 'translateY(calc(-100% - 12px))',
        transitionProperty: 'transform',
        transitionDuration: '0.15s',
        ':focus': {
            transform: 'translateY(8px)',
        },
        '@media (max-width: 640px)': {
            whiteSpace: 'normal',
            ':focus': {
                transform: `translateY(calc(${TOPBAR_HEIGHT} + 8px))`,
            },
        },
    },
    rootFill: {
        ...rootBase,
        height: '100vh',
        minHeight: 0,
        overflow: 'hidden',
    },
    rootAutoGrow: {
        ...rootBase,
        height: '100%',
        minHeight: '100%',
    },

    topbar: {
        display: 'grid',
        gridTemplateColumns: 'auto 1fr auto',
        alignItems: 'center',
        gap: 'var(--spacing-s)',
        flex: 'none',
        height: TOPBAR_HEIGHT,
        padding: '0 var(--spacing-m)',
        position: 'sticky',
        top: 0,
        zIndex: 30,
        backgroundColor: 'transparent',
        '@media (min-width: 901px)': {
            gap: 'var(--spacing-l)',
            padding: '0 var(--spacing-xl)',
        },
    },
    brand: {
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 'var(--spacing-m)',
        flex: 'none',
        width: '44px',
        height: '44px',
        border: 'none',
        backgroundColor: 'transparent',
        padding: 0,
        margin: 0,
        cursor: 'pointer',
        font: 'inherit',
        color: 'inherit',
        textAlign: 'left',
        transition: 'filter 120ms ease',
        ':hover': {
            filter: 'brightness(1.2)',
        },
        ':focus-visible': {
            outline: '2px solid var(--brand-80)',
            outlineOffset: '2px',
        },
        '@media (min-width: 901px)': {
            justifyContent: 'flex-start',
            width: 'auto',
            height: 'auto',
            minWidth: 0,
        },
    },
    brandLogo: { alignSelf: 'center', flex: 'none', color: 'var(--brand-80)' },
    brandText: {
        display: 'none',
        fontSize: 'var(--font-size-300)',
        fontWeight: 'var(--font-weight-bold)',
        whiteSpace: 'nowrap',
        backgroundImage: 'linear-gradient(116deg, var(--brand-80), var(--palette-berry-foreground))',
        WebkitBackgroundClip: 'text',
        backgroundClip: 'text',
        WebkitTextFillColor: 'transparent',
        color: 'transparent',
        '@media (min-width: 901px)': { display: 'inline' },
    },
    topbarActions: {
        gridColumn: 3,
        justifySelf: 'end',
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--spacing-s)',
    },
    topbarAction: {
        minWidth: 'var(--interactive-size-compact)',
        minHeight: 'var(--interactive-size-compact)',
        '@media (max-width: 900px), (pointer: coarse)': {
            minWidth: 'var(--interactive-size-touch)',
            minHeight: 'var(--interactive-size-touch)',
        },
    },

    shell: {
        flex: '1 1 auto',
        display: 'flex',
        alignItems: 'stretch',
        minHeight: 0,
        overflow: 'hidden',
        position: 'relative',
    },

    sidebar: {
        width: SIDEBAR_WIDTH,
        flex: 'none',
        backgroundColor: 'transparent',
        borderTopLeftRadius: 'var(--radius-xxlarge)',
        position: 'sticky',
        top: TOPBAR_HEIGHT,
        alignSelf: 'flex-start',
        height: `calc(100vh - ${TOPBAR_HEIGHT})`,
        display: 'flex',
        flexDirection: 'column',
        minHeight: 0,
        zIndex: 10,
    },
    sidebarTree: {
        flex: '1 1 auto',
        minHeight: 0,
        overflowY: 'auto',
        padding: 'var(--spacing-s) var(--spacing-s) var(--spacing-m)',
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--spacing-xxs)',
    },
    sidebarSectionLabel: {
        fontSize: 'var(--font-size-200)',
        fontWeight: 'var(--font-weight-semibold)',
        color: 'var(--neutral-foreground-3)',
        textTransform: 'uppercase',
        letterSpacing: '0.5px',
        padding: 'var(--spacing-xxs) var(--spacing-m-nudge) var(--spacing-m-nudge)',
    },
    sidebarFooter: {
        flex: 'none',
        padding: 'var(--spacing-m)',
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--spacing-s-nudge)',
        borderTop: '1px solid var(--neutral-stroke-2)',
        minWidth: 0,
        overflow: 'hidden',
    },

    main: {
        flex: '1 1 auto',
        minWidth: 0,
        minHeight: 0,
        display: 'flex',
        flexDirection: 'column',
        overflowY: 'auto',
        backgroundColor: 'var(--report-surface-canvas)',
        borderTopLeftRadius: 'var(--radius-xxlarge)',
        position: 'relative',
        zIndex: 2,
        boxShadow: 'var(--shadow-8)',
        scrollPaddingTop: 'calc(var(--eval-pivotbar-h, 49px) + var(--spacing-s))',
        ':focus': { outline: 'none' },
        ':focus-visible': {
            outline: '2px solid var(--brand-80)',
            outlineOffset: '-2px',
        },
    },
    pivotbar: {
        flex: 'none',
        position: 'sticky',
        top: 0,
        zIndex: 25,
        backgroundColor: 'var(--report-surface-canvas)',
        padding: 'var(--spacing-xs) var(--page-padding) 0',
        borderBottom: '1px solid var(--neutral-stroke-2)',
    },
    tablist: {
        display: 'flex',
        gap: 'var(--spacing-xs)',
        position: 'relative',
        justifyContent: 'space-around',
        '@media (min-width: 768px)': { justifyContent: 'space-evenly' },
        '@media (min-width: 901px)': { justifyContent: 'flex-start' },
        '@media (max-width: 640px)': {
            overflowX: 'auto',
            paddingTop: '2px',
            paddingBottom: '2px',
            scrollbarWidth: 'thin',
        },
    },
    pivotIndicator: {
        position: 'absolute',
        zIndex: 1,
        left: 0,
        bottom: 0,
        '@media (max-width: 640px)': { bottom: '2px' },
        height: '2px',
        borderRadius: 'var(--radius-circular)',
        backgroundColor: 'var(--compound-brand-background)',
        opacity: 0,
        transition:
            'transform var(--duration-normal) var(--curve-decelerate-max), ' +
            'width var(--duration-normal) var(--curve-decelerate-max)',
    },
    pivot: {
        position: 'relative',
        display: 'inline-flex',
        alignItems: 'center',
        gap: 'var(--spacing-s)',
        background: 'none',
        border: 'none',
        cursor: 'pointer',
        fontFamily: 'inherit',
        fontSize: 'var(--font-size-300)',
        padding: 'var(--spacing-m) 0',
        color: 'var(--neutral-foreground-2)',
        fontWeight: 'var(--font-weight-regular)',
        transition: 'color var(--duration-faster) var(--curve-easy-ease)',
        '&::before': {
            content: '""',
            position: 'absolute',
            left: '8px',
            right: '8px',
            bottom: 0,
            height: '2px',
            borderRadius: 'var(--radius-circular)',
            background: 'transparent',
            transition: 'background-color var(--duration-faster) var(--curve-easy-ease)',
        },
        ':hover': { color: 'var(--neutral-foreground-1)' },
        '&:hover::before': { background: 'var(--neutral-stroke-1-hover)' },
        flexShrink: 0,
        '@media (min-width: 768px)': { padding: 'var(--spacing-m) var(--spacing-s)' },
    },
    pivotActive: {
        color: 'var(--neutral-foreground-1)',
        fontWeight: 'var(--font-weight-semibold)',
    },
    pivotLabel: {
        display: 'inline-flex',
        flexDirection: 'column',
        '&::after': {
            content: 'attr(data-text)',
            fontWeight: 'var(--font-weight-semibold)',
            height: 0,
            overflow: 'hidden',
            visibility: 'hidden',
        },
    },

    contentInner: {
        boxSizing: 'border-box',
        padding: 'var(--spacing-l) var(--page-padding) var(--spacing-xxxl)',
        width: '100%',
        flex: '1 0 auto',
    },
    footer: {
        flex: 'none',
        padding: 'var(--spacing-l) var(--page-padding)',
        color: 'var(--neutral-foreground-3)',
        fontSize: 'var(--font-size-100)',
        borderTop: '1px solid var(--neutral-stroke-2)',
        backgroundColor: 'var(--report-surface-canvas)',
        borderBottomLeftRadius: 'var(--radius-xxlarge)',
    },

    scopeTrigger: {
        boxSizing: 'border-box',
        width: '100%',
        minWidth: 0,
        minHeight: '44px',
        display: 'flex',
        alignItems: 'center',
        overflow: 'hidden',
        border: 'none',
        padding: '0 var(--spacing-xxs)',
        backgroundColor: 'transparent',
        color: 'var(--brand-foreground-1)',
        font: 'inherit',
        fontSize: 'var(--font-size-300)',
        fontWeight: 'var(--font-weight-semibold)',
        textAlign: 'left',
        cursor: 'pointer',
        ':focus-visible': {
            outline: '2px solid var(--focus-stroke-outer)',
            outlineOffset: '-2px',
        },
        '@media (min-width: 901px)': { display: 'none' },
    },
    scopeLabel: {
        minWidth: 0,
        overflow: 'hidden',
        textOverflow: 'ellipsis',
        whiteSpace: 'nowrap',
        textDecorationLine: 'underline',
        textUnderlineOffset: '3px',
    },
    scopeDrawerBody: {
        display: 'flex',
        flexDirection: 'column',
        minHeight: 0,
        paddingTop: 'var(--spacing-s)',
    },
    scopeDrawerTree: {
        flex: '1 1 auto',
        minHeight: 0,
        overflowY: 'auto',
        paddingBottom: 'var(--spacing-m)',
    },
    scopeDrawerFooter: {
        flex: 'none',
        minWidth: 0,
        padding: 'var(--spacing-m) 0',
        borderTop: '1px solid var(--neutral-stroke-2)',
    },
    scopeCloseButton: {
        position: 'absolute',
        top: 'var(--spacing-m)',
        right: 'var(--spacing-m)',
        minWidth: '44px',
        minHeight: '44px',
    },

    switchLabel: { fontSize: 'var(--font-size-300)', paddingTop: 'var(--spacing-l)' },
    drawerBody: { paddingTop: 'var(--spacing-l)' },
    drawerActions: {
        marginTop: 'var(--spacing-xl)',
        paddingTop: 'var(--spacing-l)',
        borderTop: '1px solid var(--neutral-stroke-2)',
        display: 'flex',
    },
    closeButton: { position: 'absolute', top: '1.5rem', right: 'var(--spacing-l)' },
});

const TAB_ITEMS: { value: ReportView; label: string }[] = [
    { value: 'overview', label: 'Overview' },
    { value: 'cases', label: 'Cases' },
    { value: 'history', label: 'History' },
    { value: 'comparison', label: 'Comparison' },
];

const BrandMark = () => (
    <svg
        height="22"
        width="22"
        viewBox="0 0 32 32"
        xmlns="http://www.w3.org/2000/svg"
        role="presentation"
        focusable="false"
        className={useStyles().brandLogo}
    >
        <path
            clipRule="evenodd"
            fillRule="evenodd"
            fill="currentColor"
            d="M20.4052 2C20.3713 2.04989 20.3403 2.10356 20.3119 2.15906C20.1753 2.42519 20.0629 2.80022 19.9685 3.2499C19.7794 4.15205 19.6545 5.3972 19.5714 6.7798C19.405 9.54716 19.405 12.8938 19.405 15.213V24.4338L19.4049 24.4698C19.3854 27.5153 16.8918 29.9806 13.8112 29.9999L13.7749 30H3.57642C3.18062 30 2.9073 29.6141 3.04346 29.2496C4.56004 25.1917 6.6982 19.4832 8.50404 14.6901C9.40697 12.2934 10.2268 10.1257 10.8442 8.50763C11.4636 6.88453 11.876 5.82419 11.9665 5.63239C12.2132 5.10978 12.6147 4.1951 13.1873 3.40856C13.7637 2.61659 14.4808 2.00001 15.3445 2H20.4052ZM29.2769 10.1842C29.4966 10.1842 29.6747 10.3603 29.6747 10.5775V17.6706L29.6745 17.7148C29.6504 19.5836 28.1106 21.0913 26.2147 21.0913H21.668C21.6778 21.0796 21.6872 21.0676 21.6966 21.0552C21.8605 20.8367 21.9531 20.526 21.9587 20.134L21.9589 20.0958V14.0817C21.9589 11.9291 23.7238 10.1842 25.9011 10.1842H29.2769ZM21.2532 2.14424C21.5631 2.14425 21.8986 2.38926 22.2468 2.88783C22.5881 3.37681 22.9111 4.06635 23.2065 4.85721C23.7783 6.3875 24.2354 8.26487 24.5265 9.71512C22.6354 10.2861 21.2595 12.0248 21.2595 14.0817V20.0782L21.2594 20.0921C21.2575 20.2355 21.2263 20.4039 21.1685 20.5329C21.1042 20.6758 21.0375 20.7121 20.9938 20.7121C20.9575 20.7121 20.8869 20.6826 20.7852 20.5652C20.6894 20.4549 20.5915 20.2961 20.4975 20.1117C20.3151 19.7539 20.1614 19.3273 20.0739 19.0482V15.213C20.0739 8.68733 20.3039 5.39271 20.5834 3.73209C20.7239 2.89797 20.8739 2.49601 20.9998 2.30459C21.0605 2.21243 21.1101 2.17748 21.1426 2.16241C21.1755 2.14714 21.207 2.14424 21.2532 2.14424Z"
        />
    </svg>
);

const ThemeToggle = () => {
    const classes = useStyles();
    const { darkMode, setDarkMode } = useReportContext();
    const announce = useAnnounce();
    return (
        <Tooltip content={darkMode ? 'Switch to light theme' : 'Switch to dark theme'} relationship="label">
            <Button
                className={classes.topbarAction}
                appearance="subtle"
                icon={darkMode ? <WeatherSunnyRegular /> : <WeatherMoonRegular />}
                onClick={() => { const next = !darkMode; setDarkMode(next); announce(next ? 'Dark theme enabled' : 'Light theme enabled'); }}
                aria-label={darkMode ? 'Switch to light theme' : 'Switch to dark theme'}
            />
        </Tooltip>
    );
};

const PivotBar = ({ casesCount }: { casesCount: number }) => {
    const classes = useStyles();
    const { view, setView } = useReportContext();
    // Every tab must keep tabIndex={0} for the Tabster Mover — a roving tabIndex kills arrow-nav.
    const arrowNav = useArrowNavigationGroup({
        axis: 'horizontal',
        circular: true,
        memorizeCurrent: false,
        unstable_hasDefault: true,
    });
    const barRef = useRef<HTMLDivElement | null>(null);
    const trackRef = useRef<HTMLDivElement | null>(null);
    const indRef = useRef<HTMLSpanElement | null>(null);
    const placedRef = useRef(false);

    useLayoutEffect(() => {
        const bar = barRef.current;
        const port = bar?.closest('main');
        if (!bar || !port) return;
        let last = '';
        const publish = () => {
            const next = `${Math.ceil(bar.getBoundingClientRect().height)}px`;
            if (next === last) return;
            last = next;
            port.style.setProperty('--eval-pivotbar-h', next);
        };
        publish();
        if (typeof ResizeObserver === 'undefined') return;
        const ro = new ResizeObserver(publish);
        ro.observe(bar);
        return () => ro.disconnect();
    }, []);

    useLayoutEffect(() => {
        const track = trackRef.current;
        const ind = indRef.current;
        if (!track || !ind) return;
        const place = () => {
            const active = track.querySelector<HTMLElement>('[aria-selected="true"]');
            if (!active) { ind.style.opacity = '0'; return; }
            const inset = 8;
            const reduce = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
            const first = !placedRef.current;
            const prev = ind.style.transition;
            if (first || reduce) ind.style.transition = 'none';
            ind.style.opacity = '1';
            ind.style.transform = `translateX(${active.offsetLeft + inset}px)`;
            ind.style.width = `${Math.max(0, active.offsetWidth - 2 * inset)}px`;
            if (first && !reduce) {
                void ind.offsetWidth;
                requestAnimationFrame(() => { ind.style.transition = prev; });
            }
            placedRef.current = true;
        };
        place();
        const ro = new ResizeObserver(() => place());
        ro.observe(track);
        window.addEventListener('resize', place);
        return () => { ro.disconnect(); window.removeEventListener('resize', place); };
    }, [view, casesCount]);

    return (
        <div className={classes.pivotbar} ref={barRef}>
            <div
                className={classes.tablist}
                role="tablist"
                aria-label="Report views"
                ref={trackRef}
                {...arrowNav}
            >
                <span ref={indRef} className={classes.pivotIndicator} aria-hidden="true" />
                {TAB_ITEMS.map((t) => {
                    const active = view === t.value;
                    return (
                        <button
                            key={t.value}
                            type="button"
                            role="tab"
                            id={`report-tab-${t.value}`}
                            aria-selected={active}
                            aria-controls="report-tabpanel"
                            tabIndex={0}
                            data-tabster={active ? '{"focusable":{"isDefault":true}}' : undefined}
                            className={mergeClasses(classes.pivot, active && classes.pivotActive)}
                            onClick={() => setView(t.value)}
                        >
                            <span className={classes.pivotLabel} data-text={t.label}>
                                {t.label}
                            </span>
                            {t.value === 'cases' && casesCount > 0 && (
                                <Badge appearance="tint" color="brand" shape="circular">
                                    {casesCount}
                                </Badge>
                            )}
                        </button>
                    );
                })}
            </div>
        </div>
    );
};

const Sidebar = ({
    expanded,
    onToggle,
}: {
    expanded: ReadonlySet<string>;
    onToggle: (key: string) => void;
}) => {
    const classes = useStyles();
    const scenariosLabelId = useId();
    const executionLabelId = useId();
    const executionControlId = useId();

    return (
        <nav aria-label="Scenarios" className={mergeClasses(classes.sidebar, 'eval-sidebar')}>
            <div className={mergeClasses(classes.sidebarTree, 'eval-sidebar-tree')}>
                <div id={scenariosLabelId} className={classes.sidebarSectionLabel}>Scenarios</div>
                <SidebarTree labelledBy={scenariosLabelId} expanded={expanded} onToggle={onToggle} />
            </div>
            <div className={classes.sidebarFooter}>
                <span id={executionLabelId} className={classes.sidebarSectionLabel} style={{ padding: '0 var(--spacing-xxs)' }}>Execution</span>
                <ExecutionSelector labelId={executionLabelId} controlId={executionControlId} />
            </div>
        </nav>
    );
};

const ExecutionSelector = ({ labelId, controlId }: { labelId: string; controlId: string }) => {
    const { scoreSummary, setExec, activeExecution } = useReportContext();
    const executions = useMemo(
        () => [...scoreSummary.executionHistory.keys()],
        [scoreSummary],
    );

    return (
        <Dropdown
            id={controlId}
            aria-labelledby={labelId}
            className="eval-exec-drop"
            positioning={{ position: 'above', align: 'start', matchTargetSize: 'width' }}
            value={activeExecution}
            selectedOptions={[activeExecution]}
            onOptionSelect={(_ev, data) => setExec(data.optionValue)}
            button={{ children: <span className="eval-exec-text">{activeExecution}</span> }}
        >
            {executions.map((name) => (
                <Option key={name} value={name}>{name}</Option>
            ))}
        </Dropdown>
    );
};

const ScopeDrawer = ({
    open,
    onOpenChange,
    expanded,
    onToggle,
}: {
    open: boolean;
    onOpenChange: (open: boolean) => void;
    expanded: ReadonlySet<string>;
    onToggle: (key: string) => void;
}) => {
    const classes = useStyles();
    const restoreFocusSourceAttrs = useRestoreFocusSource();
    const scenariosLabelId = useId();
    const executionLabelId = useId();
    const executionControlId = useId();
    const closeButtonRef = useRef<HTMLButtonElement>(null);

    useEffect(() => {
        if (!open) return;
        const frame = requestAnimationFrame(() => closeButtonRef.current?.focus());
        return () => cancelAnimationFrame(frame);
    }, [open]);

    return (
        <Drawer
            className="eval-scope-drawer"
            open={open}
            onOpenChange={(_ev, data) => onOpenChange(data.open)}
            position="start"
            size="small"
            aria-label="Report scope"
            {...restoreFocusSourceAttrs}
        >
            <DrawerHeader>
                <Button
                    autoFocus
                    ref={closeButtonRef}
                    className={classes.scopeCloseButton}
                    icon={<DismissRegular />}
                    appearance="subtle"
                    onClick={() => onOpenChange(false)}
                    aria-label="Close report scope"
                />
            </DrawerHeader>
            <DrawerBody className={classes.scopeDrawerBody}>
                <nav aria-label="Scenarios" className={classes.scopeDrawerTree}>
                    <div id={scenariosLabelId} className={classes.sidebarSectionLabel}>Scenarios</div>
                    <SidebarTree
                        labelledBy={scenariosLabelId}
                        expanded={expanded}
                        onToggle={onToggle}
                        onSelect={() => onOpenChange(false)}
                    />
                </nav>
                <div className={classes.scopeDrawerFooter}>
                    <span id={executionLabelId} className={classes.sidebarSectionLabel}>Execution</span>
                    <ExecutionSelector labelId={executionLabelId} controlId={executionControlId} />
                </div>
            </DrawerBody>
        </Drawer>
    );
};

const SettingsDrawer = () => {
    const classes = useStyles();
    const restoreFocusSourceAttrs = useRestoreFocusSource();
    const announce = useAnnounce();
    const {
        dataset, scoreSummary,
        isSettingsOpen, setIsSettingsOpen,
        renderMarkdown, setRenderMarkdown,
        prettifyJson, setPrettifyJson,
    } = useReportContext();

    const downloadDataset = () => {
        const dataStr = JSON.stringify(dataset, null, 2);
        const blob = new Blob([dataStr], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `${scoreSummary.primaryResult.executionName}.json`;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
        announce('Report data downloaded as JSON');
    };

    return (
        <Drawer open={isSettingsOpen} onOpenChange={(_ev, data) => setIsSettingsOpen(data.open)} position="end" {...restoreFocusSourceAttrs}>
            <DrawerHeader>
                <DrawerHeaderTitle>Settings</DrawerHeaderTitle>
                <Button
                    className={classes.closeButton}
                    icon={<DismissRegular />}
                    appearance="subtle"
                    onClick={() => setIsSettingsOpen(false)}
                    aria-label="Close settings"
                />
            </DrawerHeader>
            <DrawerBody className={classes.drawerBody}>
                <Switch
                    checked={renderMarkdown}
                    onChange={(_ev, data) => setRenderMarkdown(data.checked)}
                    label={<span className={classes.switchLabel}>Render markdown for conversations</span>}
                />
                <Switch
                    checked={prettifyJson}
                    onChange={(_ev, data) => setPrettifyJson(data.checked)}
                    label={<span className={classes.switchLabel}>Pretty print JSON content</span>}
                />
                <div className={classes.drawerActions}>
                    <Button
                        appearance="secondary"
                        icon={<ArrowDownloadRegular />}
                        onClick={downloadDataset}
                    >
                        Download data as JSON
                    </Button>
                </div>
            </DrawerBody>
        </Drawer>
    );
};

const useHostTheme = (themeSource: ThemeSource, setDarkMode: (v: boolean) => void): void => {
    useEffect(() => {
        if (themeSource !== 'host') return;

        const sync = () => {
            setDarkMode(detectHostDarkMode());
        };

        sync();

        window.addEventListener('themeChanged', sync);
        return () => window.removeEventListener('themeChanged', sync);
    }, [themeSource, setDarkMode]);
};

const findNodePath = (nodes: ScoreNode[], key: string, path: ScoreNode[] = []): ScoreNode[] | undefined => {
    for (const node of nodes) {
        const nextPath = [...path, node];
        if (node.nodeKey === key) return nextPath;
        const match = findNodePath(node.childNodes, key, nextPath);
        if (match) return match;
    }
    return undefined;
};

export const AppShell = ({
    heightStrategy,
    themeSource,
    children,
}: {
    heightStrategy: HeightStrategy;
    themeSource: ThemeSource;
    children: React.ReactNode;
}) => {
    const classes = useStyles();
    const {
        dataset, scopedNode, setIsSettingsOpen,
        darkMode, setDarkMode,
        view, setView, clearScenarioLevel,
        activeNode, selectedScenarioLevel,
    } = useReportContext();
    const restoreFocusTargetAttrs = useRestoreFocusTarget();
    const scopeRestoreFocusTargetAttrs = useRestoreFocusTarget();
    const [isScopeOpen, setIsScopeOpen] = useState(false);
    const [expanded, setExpanded] = useState<ReadonlySet<string>>(
        () => new Set(activeNode.childNodes.filter((node) => node.hasChildNodes).map((node) => node.nodeKey)),
    );
    const scopeFocusTimerRef = useRef<number | undefined>(undefined);

    const toggleExpanded = useCallback((key: string) => {
        setExpanded((previous) => {
            const next = new Set(previous);
            if (next.has(key)) next.delete(key); else next.add(key);
            return next;
        });
    }, []);

    const goHome = () => {
        setView('overview');
        clearScenarioLevel();
    };
    const { fluentTheme, rootClass } = resolveTheme(darkMode);

    const casesCount =
        scopedNode.numPassingIterations +
        scopedNode.numFailingIterations;

    const resultCount = (dataset.scenarioRunResults ?? []).length;
    const executionCount = useMemo(
        () => new Set((dataset.scenarioRunResults ?? []).map((r) => r.executionName)).size,
        [dataset.scenarioRunResults],
    );

    const selectedPath = useMemo(
        () => selectedScenarioLevel ? findNodePath(activeNode.childNodes, selectedScenarioLevel) ?? [] : [],
        [activeNode, selectedScenarioLevel],
    );
    const selectedScope = selectedPath.map((node) => node.name).join('.') || 'All scenarios';

    useEffect(() => {
        const ancestors = selectedPath.slice(0, -1);
        if (ancestors.length === 0) return;
        setExpanded((previous) => {
            if (ancestors.every((node) => previous.has(node.nodeKey))) return previous;
            return new Set([...previous, ...ancestors.map((node) => node.nodeKey)]);
        });
    }, [selectedPath]);

    const restoreScopeFocus = useCallback((target: 'mobile' | 'desktop') => {
        window.clearTimeout(scopeFocusTimerRef.current);
        scopeFocusTimerRef.current = window.setTimeout(() => {
            const element = target === 'desktop'
                ? document.querySelector<HTMLElement>('.eval-sidebar [role="treeitem"][aria-selected="true"]')
                    ?? document.querySelector<HTMLElement>('.eval-sidebar [role="treeitem"]')
                : document.querySelector<HTMLElement>('.eval-scope-trigger');
            element?.focus();
        }, 300);
    }, []);

    const closeScope = useCallback((target: 'mobile' | 'desktop' = 'mobile') => {
        setIsScopeOpen(false);
        restoreScopeFocus(target);
    }, [restoreScopeFocus]);

    const setScopeOpen = useCallback((open: boolean) => {
        if (open) {
            window.clearTimeout(scopeFocusTimerRef.current);
            setIsScopeOpen(true);
        } else {
            closeScope();
        }
    }, [closeScope]);

    useEffect(() => {
        const desktop = window.matchMedia('(min-width: 901px)');
        const handleDesktop = (event: MediaQueryListEvent) => {
            if (event.matches && isScopeOpen) closeScope('desktop');
        };
        desktop.addEventListener('change', handleDesktop);
        return () => desktop.removeEventListener('change', handleDesktop);
    }, [closeScope, isScopeOpen]);

    useEffect(() => () => window.clearTimeout(scopeFocusTimerRef.current), []);

    useHostTheme(themeSource, setDarkMode);

    useAdoResize(themeSource === 'host');

    const rootClassName = heightStrategy === 'auto-grow' ? classes.rootAutoGrow : classes.rootFill;

    return (
        <FluentProvider theme={fluentTheme} className={rootClass}>
            <div className={mergeClasses('eval-root', rootClassName)}>
            <a href="#eval-main" className={classes.skipLink}>Skip to main content</a>
            <header className={mergeClasses(classes.topbar, 'eval-topbar')}>
                <button
                    type="button"
                    className={classes.brand}
                    onClick={goHome}
                    aria-label="AI Evaluation Report — go to Overview, all scenarios"
                >
                    <BrandMark />
                    <span className={classes.brandText}>AI Evaluation Report</span>
                </button>
                <button
                    type="button"
                    className={mergeClasses(classes.scopeTrigger, 'eval-scope-trigger')}
                    onClick={() => setScopeOpen(true)}
                    aria-label={`Change report scope. Current scenario: ${selectedScope}.`}
                    {...scopeRestoreFocusTargetAttrs}
                >
                    <span className={classes.scopeLabel} aria-hidden="true">{selectedScope}</span>
                </button>
                <div className={classes.topbarActions}>
                    {themeSource === 'toggle' && <ThemeToggle />}
                    <Tooltip content="Settings" relationship="label">
                        <Button className={classes.topbarAction} icon={<Settings28Regular />} appearance="subtle" onClick={() => setIsSettingsOpen(true)} aria-label="Settings" {...restoreFocusTargetAttrs} />
                    </Tooltip>
                </div>
            </header>

            <div className={mergeClasses(classes.shell, 'eval-shell')}>
                <Sidebar expanded={expanded} onToggle={toggleExpanded} />
                <main id="eval-main" tabIndex={-1} className={mergeClasses(classes.main, 'eval-main')}>
                    <h1 style={srOnlyStyle}>
                        AI Evaluation Report
                    </h1>
                    <PivotBar casesCount={casesCount} />
                    <div
                        className={mergeClasses(classes.contentInner, 'eval-content')}
                        data-screen-label="content"
                        role="tabpanel"
                        id="report-tabpanel"
                        aria-labelledby={`report-tab-${view}`}
                    >
                        {children}
                    </div>
                    <footer className={classes.footer}>
                        Generated {dataset.createdAt} · Microsoft.Extensions.AI.Evaluation.Reporting{dataset.generatorVersion ? ` ${dataset.generatorVersion}` : ''} · {resultCount} results across {executionCount} executions
                    </footer>
                </main>
            </div>

            <SettingsDrawer />
            <ScopeDrawer
                open={isScopeOpen}
                onOpenChange={setScopeOpen}
                expanded={expanded}
                onToggle={toggleExpanded}
            />
            </div>
        </FluentProvider>
    );
};
